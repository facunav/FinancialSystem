using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Imports;
using FinancialSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Imports;

internal sealed class ImportHistoryQueryService : IImportHistoryQueryService
{
    private readonly IApplicationDbContext _db;

    public ImportHistoryQueryService(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<ImportBatchSummary>> GetHistoryAsync(
        int take = 50, CancellationToken ct = default)
    {
        var batches = await _db.ImportBatches
            .AsNoTracking()
            .OrderByDescending(b => b.StartedAtUtc)
            .Take(take)
            .ToListAsync(ct);

        return batches.Select(ToSummary).ToList().AsReadOnly();
    }

    public async Task<ImportBatchDetail?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var batch = await _db.ImportBatches
            .AsNoTracking()
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

        if (batch is null) return null;

        var lines = batch.Lines
            .OrderBy(l => l.LineNumber)
            .Select(l => new ImportBatchLineSummary(l.LineNumber, l.RawText, l.Reason))
            .ToList()
            .AsReadOnly();

        return new ImportBatchDetail(ToSummary(batch), lines);
    }

    private static ImportBatchSummary ToSummary(ImportBatch b) => new(
        b.Id,
        b.SourceFile,
        b.HandlerName,
        b.StartedAtUtc,
        b.CompletedAtUtc,
        b.InsertedCount,
        b.DuplicateCount,
        b.SkippedCount,
        b.FailedCount,
        ComputeStatus(b.InsertedCount, b.FailedCount));

    private static ImportBatchStatus ComputeStatus(int inserted, int failed)
    {
        if (failed == 0) return ImportBatchStatus.Success;
        return inserted > 0 ? ImportBatchStatus.PartialSuccess : ImportBatchStatus.Failed;
    }
}
