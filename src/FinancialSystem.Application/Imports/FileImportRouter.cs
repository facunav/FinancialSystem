using System.Security.Cryptography;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Imports;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Imports;

/// <summary>
/// Enruta cada archivo al primer handler que lo acepta.
///
/// ALGORITMO:
///   Itera los handlers en el orden en que fueron registrados en DI.
///   El primer CanHandle() = true gana. Si ninguno acepta, loguea warning.
///
/// El orden de registro en DI es la única forma de controlar prioridad.
/// Los handlers más específicos deben registrarse primero.
///
/// PERSISTENCIA DE ImportBatch (PR I4):
///   Este es el único punto del pipeline por el que pasa toda importación, sin importar
///   la fuente — por eso la persistencia de ImportBatch se centraliza acá y no en cada
///   handler/importador. Un handler nuevo (IFileImportHandler) obtiene registro de
///   ImportBatch automáticamente con solo devolver un ImportRunResult, sin conocer
///   ImportBatch en absoluto.
///
/// VALIDACIÓN PREVIA (Patch 0051):
///   Antes de ofrecer el archivo a cualquier handler, IImportFileValidator confirma que
///   tiene forma válida para ser procesado (existe, no está vacío, se puede leer). Un
///   archivo rechazado acá, o uno que ningún handler reconoce, también genera un
///   ImportBatch (HandlerName "Validation") — antes, "ningún handler aceptó" no dejaba
///   ningún registro visible en el historial de importaciones.
///
/// IDEMPOTENCIA POR CONTENIDO (Patch 0052):
///   ImportBatch.ContentHash (SHA256 del archivo) ya se calculaba y persistía en cada
///   corrida desde PR I4, pero nunca se consultaba antes de procesar. Ahora, si el hash
///   ya coincide con el de un ImportBatch anterior que sí llegó a ejecutar un handler
///   real (no una fila de "Validation"/"Idempotency"), el archivo se saltea por completo
///   — ningún handler/parser lo toca, no se inserta nada — y queda registrado con
///   HandlerName "Idempotency". La detección es por contenido, no por nombre: el mismo
///   archivo con otro nombre se reconoce igual; el mismo nombre con contenido distinto
///   se procesa como corrida nueva.
///
/// CONSISTENCIA TRANSACCIONAL (Patch 0053):
///   Todo RouteAsync queda envuelto en un try/catch de nivel superior: cualquier
///   excepción no controlada en la propia orquestación del router (no solo dentro de
///   handler.HandleAsync, que ya se manejaba) se loguea una única vez y siempre termina
///   en un ImportBatch con Outcome=Failed -- nunca se propaga sin dejar rastro. Una
///   corrida Failed nunca persiste el ContentHash real del archivo (queda vacío, igual
///   que una fila de Validation): si lo hiciera, un intento fallido bloquearía para
///   siempre el reintento de ese mismo contenido (ver la idempotencia del Patch 0052).
///   La persistencia del handler y la del ImportBatch siguen siendo dos SaveChangesAsync
///   separados (cada handler administra su propio scope, ver DI: son Singleton) — lo que
///   cambia es que ningún camino de excepción queda sin clasificar ni sin registrar.
///
/// TRAZABILIDAD (Patch 0054):
///   ImportBatch gana FileSizeBytes (tamaño del archivo en bytes, calculado acá una sola
///   vez, antes de que el archivo pueda desaparecer -- ver ImportBatchEndpoints, que
///   borra el temporal apenas termina RouteAsync) y ParserUsed (identificador del parser
///   específico que procesó el archivo, reportado por el handler vía ImportRunResult;
///   distinto de HandlerName -- ver el comentario de ParserUsed en ImportRunResult).
///   Duración e identificador único no son columnas nuevas: se derivan de Id/StartedAtUtc/
///   CompletedAtUtc, que ya existían, para no duplicar información (ver
///   ImportBatch.Duration). La clasificación final de Outcome (Processed vs.
///   ProcessedWithWarnings, según Failed/Skipped &gt; 0) se centraliza acá, en un único
///   lugar, para que ningún handler tenga que decidirlo por su cuenta de forma
///   inconsistente con los demás.
/// </summary>
public class FileImportRouter : IFileImportRouter
{
    private const string ValidationHandlerName = "Validation";
    private const string IdempotencyHandlerName = "Idempotency";
    private const string RouterHandlerName = "Router";

    private readonly IReadOnlyList<IFileImportHandler> _handlers;
    private readonly IImportFileValidator _validator;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<FileImportRouter> _logger;

    public FileImportRouter(
        IEnumerable<IFileImportHandler> handlers,
        IImportFileValidator validator,
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider dateTimeProvider,
        ILogger<FileImportRouter> logger)
    {
        _handlers = handlers.ToList().AsReadOnly();
        _validator = validator;
        _scopeFactory = scopeFactory;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;

        _logger.LogInformation(
            "FileImportRouter: {Count} handlers registrados en orden: [{Handlers}]",
            _handlers.Count,
            string.Join(" → ", _handlers.Select(h => h.HandlerName)));
    }

    public async Task RouteAsync(string filePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(filePath);
        var startedAtUtc = _dateTimeProvider.UtcNow;

        // Patch 0054: se calcula una sola vez, antes de cualquier otro paso -- el archivo
        // puede dejar de existir apenas termina RouteAsync (ver ImportBatchEndpoints, que
        // borra el temporal de una subida manual en su finally), así que este es el único
        // momento confiable para registrar el tamaño.
        var fileSizeBytes = TryGetFileSizeBytes(filePath);

        try
        {
            var validation = _validator.Validate(filePath);
            if (!validation.IsValid)
            {
                _logger.LogWarning(
                    "Router: '{File}' rechazado por validación previa: {Reason}",
                    fileName, validation.RejectionReason);

                await PersistImportBatchAsync(
                    filePath, contentHash: string.Empty, ValidationHandlerName, startedAtUtc, fileSizeBytes,
                    ImportRunResult.RejectedByValidation(validation.RejectionReason ?? "Archivo inválido."),
                    ct);
                return;
            }

            var contentHash = TryComputeContentHash(filePath);

            if (!string.IsNullOrEmpty(contentHash))
            {
                var existing = await FindExistingSuccessfulBatchAsync(contentHash, ct);
                if (existing is not null)
                {
                    _logger.LogInformation(
                        "Router: '{File}' ya fue importado con el mismo contenido que '{PreviousFile}' " +
                        "({PreviousDate}) -- se saltea, no se vuelve a procesar.",
                        fileName, Path.GetFileName(existing.SourceFile), existing.CompletedAtUtc);

                    await PersistImportBatchAsync(
                        filePath, contentHash, IdempotencyHandlerName, startedAtUtc, fileSizeBytes,
                        ImportRunResult.AlreadyImported(
                            $"Este archivo ya fue importado el {existing.CompletedAtUtc:u} " +
                            $"(como '{Path.GetFileName(existing.SourceFile)}') — no se reprocesa."),
                        ct);
                    return;
                }
            }

            foreach (var handler in _handlers)
            {
                if (!handler.CanHandle(filePath))
                    continue;

                _logger.LogInformation(
                    "Router: '{File}' → [{Handler}]",
                    fileName,
                    handler.HandlerName);

                ImportRunResult result;
                try
                {
                    result = await handler.HandleAsync(filePath, ct);
                    // Centralizado acá (Patch 0054, sección 4 del patch): "Exitosa" vs.
                    // "Exitosa con advertencias" se decide en un único lugar para los tres
                    // handlers, en vez de que cada uno lo calcule por su cuenta.
                    result = ClassifyOutcome(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Router: el handler [{Handler}] lanzó una excepción no controlada procesando '{File}'",
                        handler.HandlerName, fileName);
                    result = ImportRunResult.Failure($"Excepción no controlada: {ex.Message}");
                }

                // Patch 0053: una corrida Failed nunca persiste el ContentHash real -- si
                // lo hiciera, un intento fallido bloquearía para siempre un reintento
                // futuro del mismo contenido (ver idempotencia, Patch 0052).
                var persistedHash = result.Outcome == ImportOutcome.Failed ? string.Empty : contentHash;
                await PersistImportBatchAsync(
                    filePath, persistedHash, handler.HandlerName, startedAtUtc, fileSizeBytes, result, ct);
                return;
            }

            _logger.LogWarning(
                "Router: ningún handler aceptó '{File}'. " +
                "Handlers disponibles: [{Handlers}]",
                fileName,
                string.Join(", ", _handlers.Select(h => h.HandlerName)));

            await PersistImportBatchAsync(
                filePath, contentHash, ValidationHandlerName, startedAtUtc, fileSizeBytes,
                ImportRunResult.RejectedByValidation(
                    "Ningún handler reconoce este archivo (extensión o contenido no soportado)."),
                ct);
        }
        catch (Exception ex)
        {
            // Red de seguridad final (Patch 0053): un error inesperado en la propia
            // orquestación del router (validación, cálculo de hash, búsqueda de
            // duplicados) -- no dentro de un handler, que ya se maneja arriba -- se
            // loguea una única vez acá y siempre deja un ImportBatch con Outcome=Failed,
            // en vez de propagarse sin dejar ningún registro. ContentHash vacío por el
            // mismo motivo que el caso anterior: no bloquear un reintento futuro.
            _logger.LogError(ex,
                "Router: fallo no controlado orquestando la importación de '{File}'",
                fileName);

            await PersistImportBatchAsync(
                filePath, contentHash: string.Empty, RouterHandlerName, startedAtUtc, fileSizeBytes,
                ImportRunResult.Failure($"Excepción no controlada: {ex.Message}"),
                ct);
        }
    }

    /// <summary>
    /// Único lugar (Patch 0054) donde se decide si una corrida Processed pasa a
    /// ProcessedWithWarnings -- cualquier fila omitida o fallida, sin importar qué
    /// handler la reportó, cuenta como advertencia. No toca ningún otro Outcome
    /// (RejectedByValidation/AlreadyImported/Failed ya son inequívocos por sí mismos).
    /// </summary>
    private static ImportRunResult ClassifyOutcome(ImportRunResult result) =>
        result.Outcome == ImportOutcome.Processed && (result.Failed > 0 || result.Skipped > 0)
            ? result with { Outcome = ImportOutcome.ProcessedWithWarnings }
            : result;

    /// <summary>
    /// Traduce el Outcome transitorio de ImportRunResult (capa Application) al
    /// ImportBatchOutcome persistido en la entidad de dominio (Patch 0054) -- único lugar
    /// donde ocurre esta traducción, para que el significado de cada estado final no
    /// pueda divergir entre quien lo calcula (acá) y quien lo persiste (PersistImportBatchAsync).
    /// </summary>
    private static ImportBatchOutcome MapOutcome(ImportOutcome outcome) => outcome switch
    {
        ImportOutcome.Processed => ImportBatchOutcome.Processed,
        ImportOutcome.ProcessedWithWarnings => ImportBatchOutcome.ProcessedWithWarnings,
        ImportOutcome.RejectedByValidation => ImportBatchOutcome.RejectedByValidation,
        ImportOutcome.AlreadyImported => ImportBatchOutcome.AlreadyImported,
        ImportOutcome.Failed => ImportBatchOutcome.Failed,
        _ => ImportBatchOutcome.Failed
    };

    /// <summary>Datos mínimos de un ImportBatch previo, para el mensaje de "ya importado".</summary>
    private sealed record ExistingBatchInfo(string SourceFile, DateTime CompletedAtUtc);

    /// <summary>
    /// Busca el ImportBatch más reciente con el mismo ContentHash que representa una
    /// corrida real (excluye filas "Validation"/"Idempotency", que no procesaron nada y
    /// no deben servir de base para una futura detección de duplicado).
    /// </summary>
    private async Task<ExistingBatchInfo?> FindExistingSuccessfulBatchAsync(string contentHash, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        return await db.ImportBatches
            .Where(b => b.ContentHash == contentHash
                     && b.HandlerName != ValidationHandlerName
                     && b.HandlerName != IdempotencyHandlerName
                     && b.HandlerName != RouterHandlerName)
            .OrderByDescending(b => b.CompletedAtUtc)
            .Select(b => new ExistingBatchInfo(b.SourceFile, b.CompletedAtUtc))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Persiste el ImportBatch (+ sus ImportBatchLine) de esta corrida en un único
    /// SaveChangesAsync — atómico respecto a sí mismo, pero en una transacción separada
    /// de la que ya usó el handler para persistir sus datos (ver justificación en el
    /// resumen de PR I4: ImportBatch es un registro de auditoría, no la fuente de verdad
    /// financiera). Si esto falla, se loguea pero no se relanza — los datos del handler
    /// ya quedaron guardados y no tiene sentido tratar la corrida completa como fallida
    /// por un problema al escribir el registro de auditoría.
    /// </summary>
    private async Task PersistImportBatchAsync(
        string filePath,
        string contentHash,
        string handlerName,
        DateTime startedAtUtc,
        long? fileSizeBytes,
        ImportRunResult result,
        CancellationToken ct)
    {
        var batch = new ImportBatch
        {
            SourceFile = filePath,
            ContentHash = contentHash,
            HandlerName = handlerName,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = _dateTimeProvider.UtcNow,
            FileSizeBytes = fileSizeBytes,
            ParserUsed = result.ParserUsed,
            Outcome = MapOutcome(result.Outcome),
            InsertedCount = result.Inserted,
            DuplicateCount = result.Duplicates,
            FailedCount = result.Failed,
            SkippedCount = result.Skipped
        };

        var lineNumber = 0;
        foreach (var diagnostic in result.Diagnostics)
        {
            batch.Lines.Add(new ImportBatchLine
            {
                LineNumber = ++lineNumber,
                RawText = diagnostic,
                Reason = diagnostic
            });
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.ImportBatches.Add(batch);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Router: no se pudo persistir el ImportBatch de '{File}' (handler {Handler})",
                Path.GetFileName(filePath), handlerName);
        }
    }

    private string TryComputeContentHash(string filePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Router: no se pudo calcular ContentHash para '{File}'", filePath);
            return string.Empty;
        }
    }

    /// <summary>
    /// Tamaño del archivo en bytes (Patch 0054). Null si el archivo no existe o no se
    /// puede acceder -- mismo criterio permisivo que TryComputeContentHash, para que un
    /// problema al leer metadata del archivo no aborte la corrida.
    /// </summary>
    private long? TryGetFileSizeBytes(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            return info.Exists ? info.Length : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Router: no se pudo obtener el tamaño de '{File}'", filePath);
            return null;
        }
    }
}
