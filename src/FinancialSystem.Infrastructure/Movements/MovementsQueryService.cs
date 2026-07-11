using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Movements;
using FinancialSystem.Application.Review;
using FinancialSystem.Application.Suggestions;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Movements;

internal sealed class MovementsQueryService : IMovementsQueryService
{
    private readonly IApplicationDbContext _db;
    private readonly IReviewEngine _reviewEngine;
    private readonly IClassificationSuggestionService _suggestionService;

    public MovementsQueryService(
        IApplicationDbContext db, IReviewEngine reviewEngine, IClassificationSuggestionService suggestionService)
    {
        _db = db;
        _reviewEngine = reviewEngine;
        _suggestionService = suggestionService;
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
        var pending = await LoadPendingWithWarningsAsync(from, to, cancellationToken);
        var classified = await LoadClassifiedAsync(from, to, cancellationToken);

        IEnumerable<MovementView> all = pending.Concat(classified);

        if (financialAccountId is { } accountId)
            all = all.Where(m => m.FinancialAccountId == accountId);

        if (!string.IsNullOrWhiteSpace(search))
            all = all.Where(m => m.Description.Contains(search, StringComparison.OrdinalIgnoreCase));

        return all.OrderByDescending(m => m.Date).ToList();
    }

    // K6/PR-L4: una sola llamada a IReviewEngine.GenerateAsync reemplaza lo que antes era
    // IMovementLoader.LoadAsync directo. El motor ya carga los movimientos internamente
    // (vía IMovementLoader, sin cambios) — llamar a las dos por separado habría cargado
    // el mismo período dos veces. Acá se reutiliza ese mismo resultado tanto para el
    // listado de pendientes como para los grupos sospechosos (K6, result.Suspicious) —
    // sin recalcular nada ni ejecutar el motor una segunda vez.
    //
    // PR-L4: hasta acá result traía Matched/Unmatched (sugerencias de matching contra
    // legacy, retiradas) y había que unir las dos listas para no perder movimientos con
    // match de alta confianza. Ya no hace falta: result.Movements es directamente la
    // lista completa que cargó IMovementLoader para el período.
    //
    // PR-S4: una sola llamada a IClassificationSuggestionService.SuggestAsync, con el
    // lote completo de pendientes ya filtrado a banco/tarjeta — no una por movimiento
    // (ver doc-comment de SuggestAsync). Secuencial después de IReviewEngine, mismo
    // motivo que ya explica GetAsync: comparten IApplicationDbContext, y DbContext no
    // admite operaciones concurrentes sobre la misma instancia.
    private async Task<List<MovementView>> LoadPendingWithWarningsAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var result = await _reviewEngine.GenerateAsync(from, to, cancellationToken);
        var pendingMovements = result.Movements.Where(IsBankOrCard).ToList();

        var warningsBySourceId = BuildWarningsBySourceId(result.Suspicious);
        var suggestionsBySourceId = await BuildSuggestionsBySourceIdAsync(pendingMovements, cancellationToken);

        return pendingMovements
            .Select(m => ToPendingView(
                m,
                warningsBySourceId.GetValueOrDefault(m.SourceId),
                suggestionsBySourceId.GetValueOrDefault(m.SourceId, [])))
            .ToList();
    }

    private async Task<Dictionary<Guid, IReadOnlyList<ClassificationSuggestion>>> BuildSuggestionsBySourceIdAsync(
        IReadOnlyList<FinancialMovement> pendingMovements, CancellationToken cancellationToken)
    {
        if (pendingMovements.Count == 0) return [];

        var suggestionSets = await _suggestionService.SuggestAsync(pendingMovements, cancellationToken);
        return suggestionSets.ToDictionary(s => s.SourceId, s => s.Suggestions);
    }

    private static MovementView ToPendingView(
        FinancialMovement m, MovementWarning? warning, IReadOnlyList<ClassificationSuggestion> suggestions) => new(
        m.SourceId, m.Date, m.Description, m.Amount, m.Currency, m.Source,
        m.FinancialAccountId,
        Status: null, CategoryId: null, CounterpartyId: null,
        MovementType: null, FinancialImpact: null,
        Warning: warning,
        Suggestions: suggestions);

    // PR-L4: antes ISuspicionDetector corría por separado sobre references y candidates,
    // así que un grupo nunca podía mezclar banco/tarjeta con legacy. Ahora IMovementLoader
    // solo carga banco/tarjeta (ver MovementLoader.cs), así que esa garantía se mantiene
    // por construcción — el filtro queda como chequeo defensivo, no porque haga falta hoy.
    // Un movimiento podría en teoría caer en más de un grupo (p.ej. posible duplicado Y
    // parte de un posible split a la vez) — caso raro, y esta pantalla solo necesita una
    // señal de alerta por fila, no la lista completa. Ante ese caso, gana el último grupo
    // iterado (splits sobre duplicados) — simplificación deliberada para v1.
    private static Dictionary<Guid, MovementWarning> BuildWarningsBySourceId(
        IReadOnlyList<SuspiciousGroup> suspicious)
    {
        var warningsBySourceId = new Dictionary<Guid, MovementWarning>();

        foreach (var group in suspicious)
        {
            if (group.Movements.Count == 0 || !IsBankOrCard(group.Movements[0])) continue;

            var warning = new MovementWarning(group.Reason, group.Description, group.Movements.Count);
            foreach (var m in group.Movements)
                warningsBySourceId[m.SourceId] = warning;
        }

        return warningsBySourceId;
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
                classifiedMovement.MovementType, classifiedMovement.FinancialImpact,
                // El motor solo considera pendientes (MovementLoader excluye lo
                // clasificado) — un movimiento ya clasificado nunca puede formar
                // parte de un grupo sospechoso ni recibir una sugerencia (no tiene
                // sentido sugerir algo que el usuario ya clasificó).
                Warning: null,
                Suggestions: []);
        }).ToList();
    }

    private static bool IsBankOrCard(FinancialMovement m) =>
        m.Source is MovementSource.BankDebit or MovementSource.CreditCard;
}
