using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Imports;
using Microsoft.AspNetCore.Mvc;

namespace FinancialSystem.Api.Endpoints;

public static class ImportBatchEndpoints
{
    private const int DefaultTake = 50;
    private const int MaxTake = 200;

    public static IEndpointRouteBuilder MapImportBatchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/imports").WithTags("Imports");

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

    private static async Task<IResult> Upload(
        IFormFile? file,
        [FromServices] IFileImportRouter router,
        [FromServices] IWebHostEnvironment env,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Results.BadRequest("Debe adjuntarse un archivo.");

        var extension = Path.GetExtension(file.FileName);
        if (!FileIngestionOptions.WatchedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return Results.BadRequest(
                $"Extensión no soportada '{extension}'. Soportadas: {string.Join(", ", FileIngestionOptions.WatchedExtensions)}");

        // Path.GetFileName descarta cualquier componente de directorio que venga en el
        // nombre — el archivo llega de un cliente no confiable, no se puede combinar
        // el nombre crudo en una ruta de disco.
        var safeFileName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
            return Results.BadRequest("Nombre de archivo inválido.");

        var uploadDir = Path.Combine(env.ContentRootPath, "ManualImports", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(uploadDir);
        var savedPath = Path.Combine(uploadDir, safeFileName);

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
