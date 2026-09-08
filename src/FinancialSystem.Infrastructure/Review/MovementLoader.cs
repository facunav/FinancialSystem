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

        // DEDUPE-017 (ver claude/AUDITORIA-DEDUPE017-METRICS.md, y la auditoría de
        // MovementLoader que le siguió): un candidato recién cargado acá puede
        // pertenecer a un IdentityGroupId (MovementIdentityLink) cuyo hermano físico
        // ya tiene su propio ClassifiedMovementItem. ClassifyMovementHandler nunca va
        // a dejar clasificar ese candidato -- es la misma identidad económica que su
        // hermano, ya contabilizada -- así que ofrecerlo igual como pendiente es un
        // callejón sin salida para quien revisa. Mismo criterio que ya aplica
        // FinancialMetricsService.GetClassificationCoverageAsync. No se toca ningún
        // dato: esto solo excluye filas de la lista que este método devuelve.
        var (coveredBankStatementIds, coveredTransactionIds) =
            await FindCoveredByClassifiedSiblingAsync(bankStatements, transactions, cancellationToken);

        bankStatements.RemoveAll(b => coveredBankStatementIds.Contains(b.Id));
        transactions.RemoveAll(t => coveredTransactionIds.Contains(t.Id));

        var movements = new List<FinancialMovement>(bankStatements.Count + transactions.Count);

        movements.AddRange(bankStatements.ConvertAll(ToFinancialMovement));
        movements.AddRange(transactions.ConvertAll(ToFinancialMovement));

        return movements;
    }

    /// <summary>Ids de la fuente indicada que ya tienen un ClassifiedMovementItem.</summary>
    private IQueryable<Guid> ClassifiedSourceIds(SourceEntityType sourceEntityType) => _db.ClassifiedMovementItems
        .Where(i => i.SourceEntityType == sourceEntityType)
        .Select(i => i.SourceId);

    // De los candidatos pendientes ya cargados (bankStatements/transactions), determina
    // cuáles pertenecen a un IdentityGroupId con otro miembro ya clasificado -- ver
    // comentario de LoadAsync arriba. Acotada a los ids candidatos en todo momento:
    // nunca carga ni recorre la tabla MovementIdentityLinks completa.
    //
    // Camino barato (un único SELECT, sin segunda consulta) en el caso normal: ningún
    // candidato tiene MovementIdentityLink -- no hay grupos deduplicados en el período.
    // Cuando sí los hay, una segunda consulta hace el JOIN puntual (acotado a esos
    // IdentityGroupId, nunca a toda la tabla) contra ClassifiedMovementItems para saber
    // qué grupos ya tienen algún miembro clasificado -- sin materializar el resto de
    // los miembros de cada grupo, a diferencia de un enfoque que primero trajera todos
    // los miembros y después los cruzara en memoria.
    private async Task<(HashSet<Guid> BankStatementIds, HashSet<Guid> TransactionIds)>
        FindCoveredByClassifiedSiblingAsync(
            List<BankStatement> pendingBankStatements,
            List<Transaction> pendingTransactions,
            CancellationToken cancellationToken)
    {
        if (pendingBankStatements.Count == 0 && pendingTransactions.Count == 0)
            return (new HashSet<Guid>(), new HashSet<Guid>());

        var bankStatementIds = pendingBankStatements.ConvertAll(b => b.Id);
        var transactionIds = pendingTransactions.ConvertAll(t => t.Id);

        var candidateLinks = await _db.MovementIdentityLinks
            .AsNoTracking()
            .Where(l =>
                (l.SourceEntityType == SourceEntityType.BankStatement && bankStatementIds.Contains(l.SourceId)) ||
                (l.SourceEntityType == SourceEntityType.Transaction && transactionIds.Contains(l.SourceId)))
            .Select(l => new { l.SourceEntityType, l.SourceId, l.IdentityGroupId })
            .ToListAsync(cancellationToken);

        if (candidateLinks.Count == 0)
            return (new HashSet<Guid>(), new HashSet<Guid>());

        var groupIds = candidateLinks.Select(l => l.IdentityGroupId).Distinct().ToList();

        var coveredGroupIds = (await _db.MovementIdentityLinks
            .AsNoTracking()
            .Where(l => groupIds.Contains(l.IdentityGroupId))
            .Join(
                _db.ClassifiedMovementItems.AsNoTracking(),
                l => new { l.SourceEntityType, l.SourceId },
                i => new { i.SourceEntityType, i.SourceId },
                (l, _) => l.IdentityGroupId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var coveredBankStatementIds = candidateLinks
            .Where(l => l.SourceEntityType == SourceEntityType.BankStatement && coveredGroupIds.Contains(l.IdentityGroupId))
            .Select(l => l.SourceId)
            .ToHashSet();
        var coveredTransactionIds = candidateLinks
            .Where(l => l.SourceEntityType == SourceEntityType.Transaction && coveredGroupIds.Contains(l.IdentityGroupId))
            .Select(l => l.SourceId)
            .ToHashSet();

        return (coveredBankStatementIds, coveredTransactionIds);
    }

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
        Merchant = statement.Merchant,
        MerchantAtUtc = statement.MerchantAtUtc,
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
