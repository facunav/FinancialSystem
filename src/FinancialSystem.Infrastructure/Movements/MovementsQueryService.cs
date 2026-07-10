using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Movements;
using FinancialSystem.Application.Review;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Movements;

internal sealed class MovementsQueryService : IMovementsQueryService
{
    private readonly IApplicationDbContext _db;
    private readonly IMovementLoader _movementLoader;

    public MovementsQueryService(IApplicationDbContext db, IMovementLoader movementLoader)
    {
        _db = db;
        _movementLoader = movementLoader;
    }

    public async Task<IReadOnlyList<MovementView>> GetAsync(
        DateOnly from,
        DateOnly to,
        Guid? financialAccountId,
        string? search,
        CancellationToken cancellationToken = default)
    {
        // Secuencial a propósito: ambas llamadas comparten el mismo IApplicationDbContext
        // (MovementLoader lo usa internamente), y DbContext no admite operaciones
        // concurrentes sobre la misma instancia — paralelizar con Task.WhenAll rompería
        // en tiempo de ejecución.
        var pending = await LoadPendingAsync(from, to, cancellationToken);
        var classified = await LoadClassifiedAsync(from, to, cancellationToken);

        IEnumerable<MovementView> all = pending.Concat(classified);

        if (financialAccountId is { } accountId)
            all = all.Where(m => m.FinancialAccountId == accountId);

        if (!string.IsNullOrWhiteSpace(search))
            all = all.Where(m => m.Description.Contains(search, StringComparison.OrdinalIgnoreCase));

        return all.OrderByDescending(m => m.Date).ToList();
    }

    // Una sola carga en bloque vía IMovementLoader (3 queries fijas, ya existentes) +
    // filtro en memoria a banco/tarjeta. Sin cambios a MovementLoader ni al motor.
    private async Task<List<MovementView>> LoadPendingAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var movements = await _movementLoader.LoadAsync(from, to, cancellationToken);

        return movements
            .Where(IsBankOrCard)
            .Select(m => new MovementView(
                m.SourceId, m.Date, m.Description, m.Amount, m.Currency, m.Source,
                m.FinancialAccountId,
                Status: null, CategoryId: null, CounterpartyId: null,
                MovementType: null, FinancialImpact: null))
            .ToList();
    }

    // 1 query (ClassifiedMovementItems + join a su ClassifiedMovement) + hasta 2 queries
    // en bloque (WHERE Id IN (...) sobre BankStatements/Transactions, para resolver la
    // cuenta asignada, que no vive en el snapshot). Nunca una query por fila.
    private async Task<List<MovementView>> LoadClassifiedAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var items = await _db.ClassifiedMovementItems
            .AsNoTracking()
            .Where(i => i.SourceEntityType == SourceEntityType.BankStatement
                     || i.SourceEntityType == SourceEntityType.Transaction)
            .Where(i => i.OriginalDate >= fromUtc && i.OriginalDate <= toUtc)
            .Include(i => i.ClassifiedMovement)
            .ToListAsync(cancellationToken);

        if (items.Count == 0) return [];

        var bankStatementIds = items
            .Where(i => i.SourceEntityType == SourceEntityType.BankStatement)
            .Select(i => i.SourceId)
            .ToList();
        var transactionIds = items
            .Where(i => i.SourceEntityType == SourceEntityType.Transaction)
            .Select(i => i.SourceId)
            .ToList();

        var bankAccountById = bankStatementIds.Count == 0
            ? new Dictionary<Guid, Guid?>()
            : await _db.BankStatements
                .AsNoTracking()
                .Where(b => bankStatementIds.Contains(b.Id))
                .Select(b => new { b.Id, b.FinancialAccountId })
                .ToDictionaryAsync(x => x.Id, x => x.FinancialAccountId, cancellationToken);

        var transactionAccountById = transactionIds.Count == 0
            ? new Dictionary<Guid, Guid?>()
            : await _db.Transactions
                .AsNoTracking()
                .Where(t => transactionIds.Contains(t.Id))
                .Select(t => new { t.Id, t.FinancialAccountId })
                .ToDictionaryAsync(x => x.Id, x => x.FinancialAccountId, cancellationToken);

        return items.Select(i =>
        {
            var isBankStatement = i.SourceEntityType == SourceEntityType.BankStatement;
            var financialAccountId = isBankStatement
                ? bankAccountById.GetValueOrDefault(i.SourceId)
                : transactionAccountById.GetValueOrDefault(i.SourceId);
            var source = isBankStatement ? MovementSource.BankDebit : MovementSource.CreditCard;
            var classifiedMovement = i.ClassifiedMovement!;

            return new MovementView(
                i.SourceId, i.OriginalDate, i.OriginalDescription, i.OriginalAmount, i.OriginalCurrency,
                source, financialAccountId,
                classifiedMovement.Status, classifiedMovement.CategoryId, classifiedMovement.CounterpartyId,
                classifiedMovement.MovementType, classifiedMovement.FinancialImpact);
        }).ToList();
    }

    private static bool IsBankOrCard(FinancialMovement m) =>
        m.Source is MovementSource.BankDebit or MovementSource.CreditCard;
}
