using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Review;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Review;

internal sealed class MovementLoader : IMovementLoader
{
    private readonly IApplicationDbContext _db;

    public MovementLoader(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<FinancialMovement>> LoadAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var classifiedBankStatementIds = ClassifiedSourceIds(SourceEntityType.BankStatement);
        var classifiedTransactionIds = ClassifiedSourceIds(SourceEntityType.Transaction);
        var classifiedLegacyImportIds = ClassifiedSourceIds(SourceEntityType.LegacyImport);

        var bankStatements = await _db.BankStatements
            .AsNoTracking()
            .Where(b => b.Date >= fromUtc && b.Date <= toUtc)
            .Where(b => !classifiedBankStatementIds.Contains(b.Id))
            .ToListAsync(cancellationToken);

        var transactions = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.Date >= fromUtc && t.Date <= toUtc)
            .Where(t => !classifiedTransactionIds.Contains(t.Id))
            .ToListAsync(cancellationToken);

        var legacyExpenses = await _db.LegacyImportedExpenses
            .AsNoTracking()
            .Where(e => !e.IsDiscarded)
            .Where(e => e.Date >= fromUtc && e.Date <= toUtc)
            .Where(e => !classifiedLegacyImportIds.Contains(e.Id))
            .ToListAsync(cancellationToken);

        var movements = new List<FinancialMovement>(
            bankStatements.Count + transactions.Count + legacyExpenses.Count);

        movements.AddRange(bankStatements.ConvertAll(ToFinancialMovement));
        movements.AddRange(transactions.ConvertAll(ToFinancialMovement));
        movements.AddRange(legacyExpenses.ConvertAll(ToFinancialMovement));

        return movements;
    }

    /// <summary>Ids de la fuente indicada que ya tienen un ClassifiedMovementItem.</summary>
    private IQueryable<Guid> ClassifiedSourceIds(SourceEntityType sourceEntityType) => _db.ClassifiedMovementItems
        .Where(i => i.SourceEntityType == sourceEntityType)
        .Select(i => i.SourceId);

    private static FinancialMovement ToFinancialMovement(BankStatement statement) => new()
    {
        Date = statement.Date,
        Description = statement.Concept,
        // BankStatement: positivo = crédito/ingreso, negativo = débito/egreso.
        // FinancialMovement: positivo = gasto/débito, negativo = ingreso/crédito.
        // Signo invertido a propósito al adaptar entre los dos modelos.
        Amount = -statement.Amount,
        Currency = statement.Currency,
        Source = MovementSource.BankDebit,
        OriginalId = statement.Id.ToString(),
        SourceFile = statement.SourceFile,
    };

    private static FinancialMovement ToFinancialMovement(Transaction transaction) => new()
    {
        Date = transaction.Date,
        Description = transaction.Description,
        // Transaction (extracto tarjeta): ya sigue la convención de FinancialMovement
        // (positivo = gasto/débito), sin necesidad de invertir el signo.
        Amount = transaction.Amount,
        Currency = transaction.Currency,
        Source = MovementSource.CreditCard,
        OriginalId = transaction.CouponNumber ?? transaction.Id.ToString(),
        SourceFile = transaction.SourceFile,
        RawLine = transaction.RawLine,
    };

    private static FinancialMovement ToFinancialMovement(LegacyImportedExpense expense) => new()
    {
        Date = expense.Date,
        Description = expense.Description,
        // LegacyImportedExpense: registro de gasto manual, ya sigue la convención
        // de FinancialMovement (positivo = gasto), sin necesidad de invertir el signo.
        Amount = expense.Amount,
        Currency = expense.Currency,
        Source = expense.Sheet == LegacyImportSheet.Fixed
            ? MovementSource.LegacyFixed
            : MovementSource.LegacyDynamic,
        PaymentMethod = ToPaymentMethod(expense.PaymentMethod),
        OriginalId = expense.RowNumber?.ToString() ?? expense.Id.ToString(),
        SourceFile = expense.SourceFile,
        SheetName = expense.SheetName,
    };

    private static PaymentMethod? ToPaymentMethod(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        return raw.Trim().ToLowerInvariant() switch
        {
            "debito" or "débito" => PaymentMethod.Debit,
            "credito" or "crédito" => PaymentMethod.Credit,
            "efectivo" => PaymentMethod.Cash,
            "transferencia" => PaymentMethod.Transfer,
            _ => null,
        };
    }
}
