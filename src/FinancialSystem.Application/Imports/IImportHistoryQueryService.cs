namespace FinancialSystem.Application.Imports;

// ── Modelos de resultado ──────────────────────────────────────────────────────
// Neutros: no son ImportBatch/ImportBatchLine (entidades EF), ni DTOs de HTTP.
// Existen para que el modelo interno (ImportBatch) pueda evolucionar sin romper
// el contrato público de la API — el mapeo entidad → estos modelos vive en
// ImportHistoryQueryService (Infrastructure); el mapeo modelo → DTO de HTTP vive
// en FinancialSystem.Api.DTOs (ver PR I5).

/// <summary>
/// Estado derivado de una corrida de importación. No es un campo persistido en
/// ImportBatch — se calcula a partir de sus contadores (ver ComputeStatus en
/// ImportHistoryQueryService), la misma información que ya expone la API.
/// </summary>
public enum ImportBatchStatus
{
    /// <summary>FailedCount == 0.</summary>
    Success,

    /// <summary>FailedCount > 0 pero algo se insertó.</summary>
    PartialSuccess,

    /// <summary>FailedCount > 0 y nada se insertó.</summary>
    Failed
}

public sealed record ImportBatchSummary(
    Guid Id,
    string SourceFile,
    string HandlerName,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    int InsertedCount,
    int DuplicateCount,
    int SkippedCount,
    int FailedCount,
    ImportBatchStatus Status);

public sealed record ImportBatchLineSummary(
    int LineNumber,
    string RawText,
    string Reason);

public sealed record ImportBatchDetail(
    ImportBatchSummary Batch,
    IReadOnlyList<ImportBatchLineSummary> Lines);

// ── Interfaz del servicio ─────────────────────────────────────────────────────

/// <summary>
/// Queries de solo lectura sobre el historial de importaciones (ImportBatch,
/// persistido centralizadamente por FileImportRouter — ver PR I4). Nunca persiste
/// nada. Consumida por los endpoints de /api/imports (ver PR I5).
/// </summary>
public interface IImportHistoryQueryService
{
    /// <summary>Corridas más recientes primero, hasta <paramref name="take"/> resultados.</summary>
    Task<IReadOnlyList<ImportBatchSummary>> GetHistoryAsync(
        int take = 50, CancellationToken ct = default);

    /// <summary>Detalle de una corrida puntual, incluyendo sus líneas diagnosticadas. Null si no existe.</summary>
    Task<ImportBatchDetail?> GetByIdAsync(Guid id, CancellationToken ct = default);
}
