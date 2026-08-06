using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Helpers;
using FinancialSystem.Application.Planning;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Planning;

internal sealed class PlanningMatchSuggestionService : IPlanningMatchSuggestionService
{
    private readonly IApplicationDbContext _db;

    public PlanningMatchSuggestionService(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<PlanningItemMatchSuggestion>?> GetSuggestionsAsync(
        Guid planningMonthId, CancellationToken ct = default)
    {
        var month = await _db.PlanningMonths
            .AsNoTracking()
            .Include(m => m.Items)
            .FirstOrDefaultAsync(m => m.Id == planningMonthId, ct);

        if (month is null) return null;

        var pendingItems = month.Items.Where(i => !i.IsPaid).ToList();
        if (pendingItems.Count == 0) return Array.Empty<PlanningItemMatchSuggestion>();

        // Mismo mes calendario que PlanningMonth.Period -- la épica limita la
        // integración a "movimientos importados" de ese mes, no a todo el historial.
        var monthStart = new DateTime(month.Period.Year, month.Period.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEnd = monthStart.AddMonths(1);

        var movements = await _db.ClassifiedMovements
            .AsNoTracking()
            .Where(m => m.EffectiveDate >= monthStart && m.EffectiveDate < monthEnd)
            .Select(m => new { m.Id, m.Description, m.TotalAmount, m.EffectiveDate })
            .ToListAsync(ct);

        if (movements.Count == 0) return Array.Empty<PlanningItemMatchSuggestion>();

        var suggestions = new List<PlanningItemMatchSuggestion>();
        foreach (var item in pendingItems)
        {
            var normalizedTitle = TextNormalization.Normalize(item.Title);
            if (normalizedTitle.Length == 0) continue;

            foreach (var movement in movements)
            {
                var normalizedDescription = TextNormalization.Normalize(movement.Description);
                if (normalizedDescription.Length == 0) continue;

                var matches = normalizedDescription.Contains(normalizedTitle, StringComparison.Ordinal)
                    || normalizedTitle.Contains(normalizedDescription, StringComparison.Ordinal);
                if (!matches) continue;

                suggestions.Add(new PlanningItemMatchSuggestion(
                    item.Id, item.Title, movement.Id, movement.Description, movement.TotalAmount, movement.EffectiveDate));
            }
        }

        return suggestions;
    }
}
