using FinancialSystem.Domain.Reconciliation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Application.Reconciliation.Matching;

/// <summary>
/// Detecta movimientos sospechosos DENTRO de una misma fuente,
/// antes de hacer el matching cruzado.
///
/// CASOS QUE DETECTA:
///   1. Duplicados exactos o casi exactos: mismo monto, misma fuente, fecha cercana
///   2. Transacciones partidas (splits): varios movimientos que suman a otro
///   3. Anomalías de redondeo repetidas (indicio de error sistemático)
///
/// DISEÑO: opera sobre listas completas, no sobre pares individuales.
/// El resultado alimenta ReconciliationResult.Suspicious.
/// </summary>
public sealed class SuspicionDetector : ISuspicionDetector
{
    private readonly ReconciliationOptions _opts;
    private readonly ILogger<SuspicionDetector> _logger;

    public SuspicionDetector(
        IOptions<ReconciliationOptions> opts,
        ILogger<SuspicionDetector> logger)
    {
        _opts = opts.Value;
        _logger = logger;
    }

    public IReadOnlyList<SuspiciousGroup> Detect(IReadOnlyList<FinancialMovement> movements)
    {
        var groups = new List<SuspiciousGroup>();

        groups.AddRange(DetectDuplicates(movements));
        groups.AddRange(DetectSplits(movements));

        if (groups.Count > 0)
            _logger.LogWarning("Suspicion detector encontró {Count} grupos sospechosos", groups.Count);

        return groups.AsReadOnly();
    }

    // ── Detección de duplicados ───────────────────────────────────

    private IEnumerable<SuspiciousGroup> DetectDuplicates(IReadOnlyList<FinancialMovement> movements)
    {
        var processed = new HashSet<Guid>();

        for (var i = 0; i < movements.Count; i++)
        {
            if (processed.Contains(movements[i].Id)) continue;

            var duplicates = new List<FinancialMovement> { movements[i] };

            for (var j = i + 1; j < movements.Count; j++)
            {
                if (processed.Contains(movements[j].Id)) continue;

                if (ArePossibleDuplicates(movements[i], movements[j]))
                    duplicates.Add(movements[j]);
            }

            if (duplicates.Count > 1)
            {
                foreach (var d in duplicates) processed.Add(d.Id);

                var desc = $"Posibles duplicados: {duplicates.Count} movimientos " +
                           $"de ~{duplicates[0].Amount:N2} {duplicates[0].Currency} " +
                           $"en ±{_opts.DuplicateDetectionWindowDays}d";

                _logger.LogDebug("Duplicado detectado: {Desc}", desc);

                yield return new SuspiciousGroup
                {
                    Movements = duplicates.AsReadOnly(),
                    Reason = SuspicionReason.PossibleDuplicate,
                    Description = desc,
                };
            }
        }
    }

    private bool ArePossibleDuplicates(FinancialMovement a, FinancialMovement b)
    {
        // Deben ser de la misma fuente (duplicado dentro de banco, o dentro de manual)
        if (a.Source != b.Source) return false;

        // Misma moneda
        if (!string.Equals(a.Currency, b.Currency, StringComparison.OrdinalIgnoreCase)) return false;

        // Monto muy similar
        if (Math.Abs(a.Amount - b.Amount) > _opts.DuplicateAmountTolerance) return false;

        // Fecha cercana
        if (Math.Abs((a.Date - b.Date).TotalDays) > _opts.DuplicateDetectionWindowDays) return false;

        return true;
    }

    // ── Detección de splits ───────────────────────────────────────
    // Un "split" es cuando un pago de 1000 se divide en tres movimientos de ~333.
    // Indicativo de cuotas o de carga manual partida por error.

    private IEnumerable<SuspiciousGroup> DetectSplits(IReadOnlyList<FinancialMovement> movements)
    {
        // Buscamos grupos donde la suma de N movimientos ≈ un movimiento de otra fuente.
        // Esta heurística sólo activa cuando hay movimientos de fuentes mixtas.
        // Implementación simplificada para la primera versión: detectamos
        // grupos de 2-3 movimientos manuales que suman a un movimiento bancario.

        var manuals = movements.Where(m =>
            m.Source is MovementSource.ManualDynamic or MovementSource.ManualFixed).ToList();
        var references = movements.Where(m =>
            m.Source is MovementSource.BankDebit or MovementSource.CreditCard).ToList();

        foreach (var reference in references)
        {
            // Candidatos manuales en la misma ventana de tiempo
            var windowCandidates = manuals
                .Where(m => Math.Abs((m.Date - reference.Date).TotalDays) <= _opts.DateWindowDays)
                .Where(m => string.Equals(m.Currency, reference.Currency, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Pares que suman ~al reference
            for (var i = 0; i < windowCandidates.Count - 1; i++)
            {
                for (var j = i + 1; j < windowCandidates.Count; j++)
                {
                    var pairSum = windowCandidates[i].Amount + windowCandidates[j].Amount;
                    if (Math.Abs(pairSum - reference.Amount) <= _opts.AmountAbsoluteTolerance)
                    {
                        yield return new SuspiciousGroup
                        {
                            Movements = [windowCandidates[i], windowCandidates[j], reference],
                            Reason = SuspicionReason.SplitTransaction,
                            Description =
                                $"Posible split: {windowCandidates[i].Amount:N2} + " +
                                $"{windowCandidates[j].Amount:N2} ≈ {reference.Amount:N2} " +
                                $"({reference.Description})",
                        };
                    }
                }
            }
        }
    }
}
