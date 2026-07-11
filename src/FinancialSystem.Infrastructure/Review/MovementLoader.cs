using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Review;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Review;

// PR-L4: hasta acá este loader también cargaba LegacyImportedExpense, como candidato
// para el motor de matching (ver ReviewEngine.cs). Ese mecanismo se retiró completo y
// el motor dejó de leerla. PR-L5: LegacyImportedExpense (la entidad y su tabla) se
// eliminó del sistema — este loader queda exclusivamente banco/tarjeta.
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

        var movements = new List<FinancialMovement>(bankStatements.Count + transactions.Count);

        movements.AddRange(bankStatements.ConvertAll(ToFinancialMovement));
        movements.AddRange(transactions.ConvertAll(ToFinancialMovement));

        return movements;
    }

    /// <summary>Ids de la fuente indicada que ya tienen un ClassifiedMovementItem.</summary>
    private IQueryable<Guid> ClassifiedSourceIds(SourceEntityType sourceEntityType) => _db.ClassifiedMovementItems
        .Where(i => i.SourceEntityType == sourceEntityType)
        .Select(i => i.SourceId);

    private static FinancialMovement ToFinancialMovement(BankStatement statement) => new()
    {
        SourceId = statement.Id,
        Date = statement.Date,
        Description = statement.Concept,
        // BankStatement: positivo = crédito/ingreso, negativo = débito/egreso.
        // FinancialMovement: positivo = gasto/débito, negativo = ingreso/crédito.
        // Signo invertido a propósito al adaptar entre los dos modelos.
        Amount = -statement.Amount,
        Currency = statement.Currency,
        Source = MovementSource.BankDebit,
        OriginalId = statement.RowNumber?.ToString(),
        SourceFile = statement.SourceFile,
        FinancialAccountId = statement.FinancialAccountId,
    };

    private static FinancialMovement ToFinancialMovement(Transaction transaction) => new()
    {
        SourceId = transaction.Id,
        Date = transaction.Date,
        Description = transaction.Description,
        // Transaction (extracto tarjeta): ya sigue la convención de FinancialMovement
        // (positivo = gasto/débito), sin necesidad de invertir el signo.
        Amount = transaction.Amount,
        Currency = transaction.Currency,
        Source = MovementSource.CreditCard,
        OriginalId = transaction.CouponNumber,
        SourceFile = transaction.SourceFile,
        RawLine = transaction.RawLine,
        FinancialAccountId = transaction.FinancialAccountId,
    };

}
