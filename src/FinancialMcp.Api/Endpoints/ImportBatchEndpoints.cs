using FinancialSystem.Api.DTOs;
using FinancialSystem.Api.Imports;
using FinancialSystem.Application.Imports;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Api.Endpoints;

public static class ImportBatchEndpoints
{
    private const int DefaultTake = 50;
    private const int MaxTake = 200;

    public static IEndpointRouteBuilder MapImportBatchEndpoints(this IEndpointRouteBuilder app)
    {
        // Patch 0059 (PATCH-010): protegido con la infraestructura de autenticación del
        // Patch 0058 -- subir archivos e iniciar importaciones, y consultar el historial,
        // son operaciones sobre datos financieros. RequireAuthorization() en el grupo
        // cubre las 3 rutas sin repetir la llamada por endpoint (ver ApiKeyAuthenticationHandler).
        var group = app.MapGroup("/api/imports").WithTags("Imports").RequireAuthorization();

        group.MapGet("/history", GetHistory);
        group.MapGet("/{id:guid}", GetById);
        group.MapPost("/", Upload).DisableAntiforgery();

        return app;
    }

    // ── POST /api/imports (multipart/form-data, campo "file") ──────────────────
    //
    // PR-O1 (Épica O): segundo punto de entrada al mismo motor de importación que ya
    // usa el Worker (IFileImportRouter → Handlers → Parsers → Importers, sin cambios).
    // El archivo se guarda en una carpeta propia (no la que observa el Worker) para que
    // el watcher nunca lo vea y no se procese dos veces. RouteAsync se espera por
    // completo antes de responder — para este PR, ver el resultado significa consultar
    // GET /api/imports/history después, igual que ya hace imports.html hoy.
    //
    // El archivo solo necesita existir mientras dura RouteAsync (el propio motor de
    // importación no reabre SourceFile una vez terminado — ver
    // docs/Epics/EpicaO-ImportacionManual.md). No hay ningún beneficio hoy en
    // conservarlo, así que se borra siempre en el finally, haya salido bien o mal la
    // importación. El borrado es best-effort: si falla, se loguea y no afecta el
    // resultado ya devuelto al handler.
    //
    // Patch 0063 (PATCH-014): validaciones agregadas antes de tocar disco o llamar al
    // router -- tamaño, extensión y "magic bytes" -- en ese orden, todas devolviendo el
    // mismo Results.BadRequest(string) que ya usaba este endpoint (sin excepciones sin
    // controlar, sin cambiar el formato de respuesta). Nada de esto toca
    // IFileImportRouter/los parsers/los importadores: si un archivo pasa las cuatro
    // validaciones, el resto del flujo es exactamente el mismo que antes del patch.

    private static async Task<IResult> Upload(
        IFormFile? file,
        [FromServices] IFileImportRouter router,
        [FromServices] IWebHostEnvironment env,
        [FromServices] IOptions<ImportUploadOptions> uploadOptions,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Results.BadRequest("Debe adjuntarse un archivo.");

        var maxFileSizeBytes = uploadOptions.Value.MaxFileSizeBytes;
        if (file.Length > maxFileSizeBytes)
            return Results.BadRequest(
                $"El archivo supera el tamaño máximo permitido ({maxFileSizeBytes / (1024 * 1024)} MB).");

        var extension = Path.GetExtension(file.FileName);
        if (!FileIngestionOptions.WatchedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return Results.BadRequest(
                $"Extensión no soportada '{extension}'. Soportadas: {string.Join(", ", FileIngestionOptions.WatchedExtensions)}");

        if (!await HasExpectedSignatureAsync(file, extension, ct))
            return Results.BadRequest(
                $"El contenido del archivo no coincide con la extensión declarada ('{extension}').");

        // Path.GetFileName descarta cualquier componente de directorio que venga en el
        // nombre — el archivo llega de un cliente no confiable, no se puede combinar
        // el nombre crudo en una ruta de disco.
        var safeFileName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
            return Results.BadRequest("Nombre de archivo inválido.");

        var tempDir = Path.Combine(env.ContentRootPath, "TempImports", Guid.NewGuid().ToString("N"));
        var savedPath = Path.Combine(tempDir, safeFileName);

        try
        {
            Directory.CreateDirectory(tempDir);

            await using (var stream = new FileStream(savedPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await file.CopyToAsync(stream, ct);
            }

            await router.RouteAsync(savedPath, ct);

            return Results.Ok(new
            {
                FileName = safeFileName,
                Message = "Importación procesada. Consultá el historial (/api/imports/history) para ver el resultado."
            });
        }
        finally
        {
            TryDeleteTempImport(savedPath, tempDir, logger);
        }
    }

    // Lee solo el encabezado del archivo (8 bytes alcanza para las cuatro extensiones
    // soportadas) para chequear la firma sin cargar el archivo completo en memoria.
    // IFormFile ya llega completamente bufferizado (Length está disponible de forma
    // sincrónica más arriba) -- OpenReadStream() puede volver a abrirse más abajo
    // (CopyToAsync) de forma independiente, sin depender de la posición en la que
    // quede este stream.
    private static async Task<bool> HasExpectedSignatureAsync(IFormFile file, string extension, CancellationToken ct)
    {
        const int headerLength = 8;
        var header = new byte[headerLength];

        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(header.AsMemory(0, headerLength), ct);

        return ImportFileSignatureValidator.HasExpectedSignature(extension, header.AsSpan(0, read));
    }

    private static void TryDeleteTempImport(string filePath, string tempDir, ILogger logger)
    {
        // Archivo y directorio se intentan por separado: que uno falle no debe impedir
        // el intento del otro, y cada fallo se loguea con el path que realmente
        // corresponde (para poder distinguir, por ejemplo, un antivirus reteniendo el
        // archivo de un problema de permisos sobre el directorio).
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo eliminar el archivo temporal de importación '{FilePath}'", filePath);
        }

        try
        {
            // Solo borra el directorio temporal si quedó vacío.
            if (Directory.Exists(tempDir) && !Directory.EnumerateFileSystemEntries(tempDir).Any())
                Directory.Delete(tempDir);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo eliminar el directorio temporal de importación '{TempDir}'", tempDir);
        }
    }

    // ── GET /api/imports/history?take=50 ──────────────────────────────────────

    private static async Task<IResult> GetHistory(
        [FromQuery] int? take,
        [FromServices] IImportHistoryQueryService service,
        CancellationToken ct)
    {
        var effectiveTake = take.GetValueOrDefault(DefaultTake);
        if (effectiveTake is < 1 or > MaxTake)
            return Results.BadRequest($"'take' debe estar entre 1 y {MaxTake}");

        var batches = await service.GetHistoryAsync(effectiveTake, ct);
        return Results.Ok(ImportHistoryResponse.Create(batches));
    }

    // ── GET /api/imports/{id} ─────────────────────────────────────────────────

    private static async Task<IResult> GetById(
        Guid id,
        [FromServices] IImportHistoryQueryService service,
        CancellationToken ct)
    {
        var detail = await service.GetByIdAsync(id, ct);
        return detail is null
            ? Results.NotFound($"No existe un ImportBatch con id {id}")
            : Results.Ok(ImportBatchDetailDto.Create(detail));
    }
}
