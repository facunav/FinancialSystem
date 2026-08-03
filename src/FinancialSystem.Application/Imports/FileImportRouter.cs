using System.Security.Cryptography;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Imports;
using FinancialSystem.Domain.Entities;
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
/// </summary>
public class FileImportRouter : IFileImportRouter
{
    private const string ValidationHandlerName = "Validation";
    private const string IdempotencyHandlerName = "Idempotency";

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

        var validation = _validator.Validate(filePath);
        if (!validation.IsValid)
        {
            _logger.LogWarning(
                "Router: '{File}' rechazado por validación previa: {Reason}",
                fileName, validation.RejectionReason);

            await PersistImportBatchAsync(
                filePath, contentHash: string.Empty, ValidationHandlerName, startedAtUtc,
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
                    filePath, contentHash, IdempotencyHandlerName, startedAtUtc,
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Router: el handler [{Handler}] lanzó una excepción no controlada procesando '{File}'",
                    handler.HandlerName, fileName);
                result = ImportRunResult.Failure($"Excepción no controlada: {ex.Message}");
            }

            await PersistImportBatchAsync(filePath, contentHash, handler.HandlerName, startedAtUtc, result, ct);
            return;
        }

        _logger.LogWarning(
            "Router: ningún handler aceptó '{File}'. " +
            "Handlers disponibles: [{Handlers}]",
            fileName,
            string.Join(", ", _handlers.Select(h => h.HandlerName)));

        await PersistImportBatchAsync(
            filePath, contentHash, ValidationHandlerName, startedAtUtc,
            ImportRunResult.RejectedByValidation(
                "Ningún handler reconoce este archivo (extensión o contenido no soportado)."),
            ct);
    }

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
                     && b.HandlerName != IdempotencyHandlerName)
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
}
