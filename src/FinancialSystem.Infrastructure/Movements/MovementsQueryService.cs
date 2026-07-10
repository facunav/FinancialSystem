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
    private readonly IReviewEngine _reviewEngine;

    public MovementsQueryService(IApplicationDbContext db, IReviewEngine reviewEngine)
    {
        _db = db;
        _reviewEngine = reviewEngine;
    }

    public async Task<IReadOnlyList<MovementView>> GetAsync(
        DateOnly from,
        DateOnly to,
        Guid? financialAccountId,
        string? search,
        CancellationToken cancellationToken = default)
    {
        // Secuencial a propósito: ambas llamadas comparten el mismo IApplicationDbContext
        // (IReviewEngine lo usa internamente vía IMovementLoader), y DbContext no admite
        // operaciones concurrentes sobre la misma instancia — paralelizar con
        // Task.WhenAll rompería en tiempo de ejecución.
        var pending = await LoadPendingWithSuggestionsAsync(from, to, cancellationToken);
        var classified = await LoadClassifiedAsync(from, to, cancellationToken);

        IEnumerable<MovementView> all = pending.Concat(classified);

        if (financialAccountId is { } accountId)
            all = all.Where(m => m.FinancialAccountId == accountId);

        if (!string.IsNullOrWhiteSpace(search))
            all = all.Where(m => m.Description.Contains(search, StringComparison.OrdinalIgnoreCase));

        return all.OrderByDescending(m => m.Date).ToList();
    }

    // K4: una sola llamada a IReviewEngine.GenerateAsync reemplaza lo que antes era
    // IMovementLoader.LoadAsync directo. El motor ya carga los movimientos internamente
    // (vía IMovementLoader, sin cambios) — llamar a las dos por separado habría cargado
    // el mismo período dos veces y, peor, tirado a la basura el cálculo de matching que
    // GenerateAsync ya hace. Acá se reutiliza ese mismo resultado tanto para reconstruir
    // el listado de pendientes (Matched.Reference + Unmatched, ver abajo) como para las
    // sugerencias — sin recalcular nada ni ejecutar el motor una segunda vez.
    //
    // Reconstrucción del listado: el motor separa lo asignado (Matched) de lo que no
    // (Unmatched) — un movimiento de banco/tarjeta está en uno de los dos, nunca en
    // ambos ni en ninguno. Tomar solo Unmatched dejaría afuera de la lista cualquier
    // movimiento con match de alta confianza — Movimientos sigue siendo una lista
    // completa de movimientos, con la sugerencia como dato adicional, no un filtro.
    private async Task<List<MovementView>> LoadPendingWithSuggestionsAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var result = await _reviewEngine.GenerateAsync(from, to, cancellationToken);
        var views = new List<MovementView>();

        foreach (var pair in result.Matched)
        {
            if (!IsBankOrCard(pair.Reference)) continue;
            views.Add(ToPendingView(pair.Reference, ToSuggestion(pair.Candidate, pair.Confidence)));
        }

        foreach (var unmatched in result.Unmatched)
        {
            if (!IsBankOrCard(unmatched.Movement)) continue;
            var bestNearMiss = unmatched.NearMisses.Count > 0 ? unmatched.NearMisses[0] : null;
            var suggestion = bestNearMiss is null
                ? null
                : ToSuggestion(bestNearMiss.Candidate, bestNearMiss.Confidence);
            views.Add(ToPendingView(unmatched.Movement, suggestion));
        }

        return views;
    }

    private static MovementView ToPendingView(FinancialMovement m, MovementSuggestion? suggestion) => new(
        m.SourceId, m.Date, m.Description, m.Amount, m.Currency, m.Source,
        m.FinancialAccountId,
        Status: null, CategoryId: null, CounterpartyId: null,
        MovementType: null, FinancialImpact: null,
        Suggestion: suggestion);

    private static MovementSuggestion ToSuggestion(FinancialMovement candidate, MatchConfidence confidence) => new(
        candidate.SourceId, candidate.Description, candidate.Amount, candidate.Date, confidence);

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
                classifiedMovement.MovementType, classifiedMovement.FinancialImpact,
                // El motor solo considera pendientes (MovementLoader excluye lo
                // clasificado) — un movimiento ya clasificado nunca tiene sugerencia.
                Suggestion: null);
        }).ToList();
    }

    private static bool IsBankOrCard(FinancialMovement m) =>
        m.Source is MovementSource.BankDebit or MovementSource.CreditCard;
}
