using FinancialSystem.Application.Review;
using FinancialSystem.Domain.Review;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Infrastructure.Review;

/// <summary>
/// Detecta posibles duplicados (movimientos con monto y fecha muy cercanos entre sí)
/// y posibles splits (varios movimientos cuya suma iguala el monto de otro) dentro
/// de una misma lista de movimientos. Reutiliza las tolerancias de duplicados de
/// <see cref="ReviewEngineOptions"/> también como ventana/tolerancia para splits,
/// ya que ambos son casos de "movimientos cercanos en el tiempo dentro de la misma fuente".
/// </summary>
internal sealed class SuspicionDetector : ISuspicionDetector
{
    // Acota las combinaciones evaluadas por split para evitar una explosión
    // combinatoria cuando muchos movimientos caen en la misma ventana de fecha.
    private const int MaxSplitPartsConsidered = 12;

    private readonly ReviewEngineOptions _options;

    public SuspicionDetector(IOptions<ReviewEngineOptions> options) => _options = options.Value;

    public IReadOnlyList<SuspiciousGroup> Detect(IReadOnlyList<FinancialMovement> movements)
    {
        var groups = new List<SuspiciousGroup>();
        groups.AddRange(DetectDuplicates(movements));
        groups.AddRange(DetectSplits(movements));
        return groups;
    }

    // ── Duplicados ──────────────────────────────────────────────────────────

    private List<SuspiciousGroup> DetectDuplicates(IReadOnlyList<FinancialMovement> movements)
    {
        var adjacency = movements.ToDictionary(m => m.Id, _ => new List<FinancialMovement>());

        for (var i = 0; i < movements.Count; i++)
        {
            for (var j = i + 1; j < movements.Count; j++)
            {
                var a = movements[i];
                var b = movements[j];
                if (!IsPossibleDuplicate(a, b)) continue;

                adjacency[a.Id].Add(b);
                adjacency[b.Id].Add(a);
            }
        }

        var visited = new HashSet<Guid>();
        var groups = new List<SuspiciousGroup>();

        foreach (var movement in movements)
        {
            if (visited.Contains(movement.Id)) continue;

            var component = CollectComponent(movement, adjacency, visited);
            if (component.Count < 2) continue;

            var averageAmount = component.Average(m => Math.Abs(m.Amount));
            groups.Add(new SuspiciousGroup
            {
                Movements = component,
                Reason = SuspicionReason.PossibleDuplicate,
                Description =
                    $"{component.Count} movimientos con monto similar (~{averageAmount:C}) " +
                    $"dentro de {_options.DuplicateDetectionWindowDays} día(s).",
            });
        }

        return groups;
    }

    private bool IsPossibleDuplicate(FinancialMovement a, FinancialMovement b)
    {
        var amountDiff = Math.Abs(Math.Abs(a.Amount) - Math.Abs(b.Amount));
        if (amountDiff > _options.DuplicateAmountTolerance) return false;

        var daysDiff = Math.Abs((a.Date.Date - b.Date.Date).Days);
        return daysDiff <= _options.DuplicateDetectionWindowDays;
    }

    private static List<FinancialMovement> CollectComponent(
        FinancialMovement start,
        IReadOnlyDictionary<Guid, List<FinancialMovement>> adjacency,
        HashSet<Guid> visited)
    {
        var component = new List<FinancialMovement>();
        var stack = new Stack<FinancialMovement>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current.Id)) continue;

            component.Add(current);
            foreach (var neighbor in adjacency[current.Id])
                if (!visited.Contains(neighbor.Id))
                    stack.Push(neighbor);
        }

        return component;
    }

    // ── Splits ──────────────────────────────────────────────────────────────

    private List<SuspiciousGroup> DetectSplits(IReadOnlyList<FinancialMovement> movements)
    {
        var groups = new List<SuspiciousGroup>();
        var alreadyMatchedAsTotal = new HashSet<Guid>();

        foreach (var total in movements)
        {
            if (alreadyMatchedAsTotal.Contains(total.Id)) continue;

            var parts = movements
                .Where(m => m.Id != total.Id)
                .Where(m => Math.Abs((m.Date.Date - total.Date.Date).Days) <= _options.DuplicateDetectionWindowDays)
                .Take(MaxSplitPartsConsidered)
                .ToList();

            var split = FindSplit(total, parts);
            if (split is null) continue;

            alreadyMatchedAsTotal.Add(total.Id);

            var groupMovements = new List<FinancialMovement> { total };
            groupMovements.AddRange(split);

            groups.Add(new SuspiciousGroup
            {
                Movements = groupMovements,
                Reason = SuspicionReason.SplitTransaction,
                Description =
                    $"{split.Count} movimientos suman {split.Sum(m => Math.Abs(m.Amount)):C}, " +
                    $"igual al monto de otro movimiento de {Math.Abs(total.Amount):C}.",
            });
        }

        return groups;
    }

    /// <summary>Busca 2 o 3 movimientos entre "parts" cuya suma iguale total.Amount (con tolerancia).</summary>
    private List<FinancialMovement>? FindSplit(FinancialMovement total, IReadOnlyList<FinancialMovement> parts)
    {
        var targetAmount = Math.Abs(total.Amount);

        for (var i = 0; i < parts.Count; i++)
        {
            for (var j = i + 1; j < parts.Count; j++)
            {
                if (IsCloseEnough(targetAmount, Math.Abs(parts[i].Amount) + Math.Abs(parts[j].Amount)))
                    return new List<FinancialMovement> { parts[i], parts[j] };

                for (var k = j + 1; k < parts.Count; k++)
                {
                    var sum = Math.Abs(parts[i].Amount) + Math.Abs(parts[j].Amount) + Math.Abs(parts[k].Amount);
                    if (IsCloseEnough(targetAmount, sum))
                        return new List<FinancialMovement> { parts[i], parts[j], parts[k] };
                }
            }
        }

        return null;
    }

    private bool IsCloseEnough(decimal target, decimal sum) =>
        Math.Abs(target - sum) <= _options.DuplicateAmountTolerance;
}
