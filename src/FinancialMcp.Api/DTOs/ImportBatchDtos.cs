using FinancialSystem.Application.Imports;

namespace FinancialSystem.Api.DTOs;

// ── GET /api/imports/history ─────────────────────────────────────────────────

public sealed record ImportBatchSummaryDto(
    Guid Id,
    string SourceFile,
    string HandlerName,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    int InsertedCount,
    int DuplicateCount,
    int SkippedCount,
    int FailedCount,
    string Status)
{
    public static ImportBatchSummaryDto Create(ImportBatchSummary s) => new(
        s.Id,
        s.SourceFile,
        s.HandlerName,
        s.StartedAtUtc,
        s.CompletedAtUtc,
        s.InsertedCount,
        s.DuplicateCount,
        s.SkippedCount,
        s.FailedCount,
        s.Status.ToString());
}

public sealed record ImportHistoryResponse(IReadOnlyList<ImportBatchSummaryDto> Batches)
{
    public static ImportHistoryResponse Create(IReadOnlyList<ImportBatchSummary> batches) =>
        new(batches.Select(ImportBatchSummaryDto.Create).ToList());
}

// ── GET /api/imports/{id} ────────────────────────────────────────────────────

public sealed record ImportBatchLineDto(int LineNumber, string RawText, string Reason)
{
    public static ImportBatchLineDto Create(ImportBatchLineSummary l) =>
        new(l.LineNumber, l.RawText, l.Reason);
}

public sealed record ImportBatchDetailDto(
    ImportBatchSummaryDto Batch,
    IReadOnlyList<ImportBatchLineDto> Lines)
{
    public static ImportBatchDetailDto Create(ImportBatchDetail d) => new(
        ImportBatchSummaryDto.Create(d.Batch),
        d.Lines.Select(ImportBatchLineDto.Create).ToList());
}
