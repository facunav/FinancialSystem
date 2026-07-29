using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Movements;
using FinancialSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Movements;

internal sealed class MovementLookupService : IMovementLookupService
{
    private readonly IApplicationDbContext _db;

    public MovementLookupService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<MovementDetail?> GetBySourceAsync(
        SourceEntityType sourceEntityType, Guid sourceId, CancellationToken cancellationToken = default)
    {
        var baseDetail = sourceEntityType switch
        {
            SourceEntityType.Transaction => await LoadTransactionAsync(sourceId, cancellationToken),
            SourceEntityType.BankStatement => await LoadBankStatementAsync(sourceId, cancellationToken),
            // PR-L5: LegacyImportedExpense se eliminó -- no queda tabla de origen detrás de
            // SourceEntityType.LegacyImport (mismo caso ya documentado en
            // ClassifyMovementHandler.FindSourceAsync).
            _ => null,
        };

        if (baseDetail is null) return null;

        var classification = await LoadClassificationAsync(sourceEntityType, sourceId, cancellationToken);
        return baseDetail with { Classification = classification };
    }

    private async Task<MovementDetail?> LoadTransactionAsync(Guid id, CancellationToken ct)
    {
        var t = await _db.Transactions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return null;

        var accountName = await ResolveFinancialAccountNameAsync(t.FinancialAccountId, ct);

        return new MovementDetail(
            SourceEntityType.Transaction, t.Id, t.Date, t.Description, t.Amount, t.Currency,
            SourceFile: t.SourceFile,
            FinancialAccountId: t.FinancialAccountId,
            FinancialAccountName: accountName,
            ExternalId: t.ExternalId,
            CouponNumber: t.CouponNumber,
            RawLine: t.RawLine,
            BankName: null,
            AccountNumber: null,
            BankDetail: null,
            Balance: null,
            SheetName: null,
            RowNumber: null,
            Merchant: null,
            MerchantAtUtc: null,
            SourceRecordedAtUtc: t.CreatedAtUtc,
            Classification: null);
    }

    private async Task<MovementDetail?> LoadBankStatementAsync(Guid id, CancellationToken ct)
    {
        var b = await _db.BankStatements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null) return null;

        var accountName = await ResolveFinancialAccountNameAsync(b.FinancialAccountId, ct);

        return new MovementDetail(
            SourceEntityType.BankStatement, b.Id, b.Date, b.Concept, b.Amount, b.Currency,
            SourceFile: b.SourceFile,
            FinancialAccountId: b.FinancialAccountId,
            FinancialAccountName: accountName,
            ExternalId: b.ExternalId,
            CouponNumber: null,
            RawLine: null,
            BankName: b.BankName,
            AccountNumber: b.AccountNumber,
            BankDetail: b.Detail,
            Balance: b.Balance,
            SheetName: b.SheetName,
            RowNumber: b.RowNumber,
            Merchant: b.Merchant,
            MerchantAtUtc: b.MerchantAtUtc,
            SourceRecordedAtUtc: b.ImportedAtUtc,
            Classification: null);
    }

    private async Task<string?> ResolveFinancialAccountNameAsync(Guid? accountId, CancellationToken ct)
    {
        if (accountId is not { } id) return null;

        return await _db.FinancialAccounts
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => a.Name)
            .FirstOrDefaultAsync(ct);
    }

    // Mismo predicado y mismos Include que ya usa ClassifyMovementHandler.Handle para
    // encontrar la clasificación (si existe) de un origen -- ver ese archivo.
    private async Task<MovementClassificationDetail?> LoadClassificationAsync(
        SourceEntityType sourceEntityType, Guid sourceId, CancellationToken ct)
    {
        var item = await _db.ClassifiedMovementItems
            .AsNoTracking()
            .Include(i => i.ClassifiedMovement)
            .ThenInclude(cm => cm!.Items)
            .FirstOrDefaultAsync(
                i => i.SourceEntityType == sourceEntityType && i.SourceId == sourceId, ct);

        if (item is null) return null;

        var cm = item.ClassifiedMovement!;

        var categoryName = await _db.Categories
            .AsNoTracking()
            .Where(c => c.Id == cm.CategoryId)
            .Select(c => c.DisplayName)
            .FirstOrDefaultAsync(ct);

        var counterpartyName = cm.CounterpartyId is { } counterpartyId
            ? await _db.Counterparties
                .AsNoTracking()
                .Where(c => c.Id == counterpartyId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(ct)
            : null;

        var groupItems = cm.Items
            .Select(i => new MatchGroupItem(i.SourceEntityType, i.SourceId, i.Role))
            .ToList();

        return new MovementClassificationDetail(
            cm.Id, cm.EffectiveDate, cm.CategoryId, categoryName, cm.CounterpartyId, counterpartyName,
            cm.MovementType, cm.FinancialImpact, cm.Status, cm.ProcessingSource, cm.Comment,
            cm.MatchScore, cm.AmountDelta, cm.CreatedAt, cm.ProcessedAt, cm.ProcessedBy,
            item.Role, groupItems);
    }
}
