using FinancialSystem.Domain.Reconciliation;

namespace FinancialSystem.Application.Reconciliation;

/// <summary>
/// Resultado en memoria de una ejecución de conciliación.
/// Clasifica los MatchedPairs por nivel de confianza.
///
/// CICLO DE VIDA:
///   Este objeto vive únicamente en memoria durante una request/operación.
///   No se persiste. El llamador elige qué hacer con cada grupo:
///     - AutoConfirmable → candidatos para confirmación en batch
///     - NeedsReview     → mostrar al usuario para decisión manual
///     - Ignored         → confianza demasiado baja, descartar
///     - Unmatched       → movimientos sin contraparte encontrada
///
/// PERSISTENCIA:
///   Nada de este objeto va a la DB directamente.
///   El flujo de confirmación (punto 3) toma MatchedPairs específicos
///   y los convierte en ReconciledExpense + ReconciledExpenseItems.
/// </summary>
public sealed record ReconciliationSuggestions
{
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }

    /// <summary>
    /// Alta confianza (score ≥ HighConfidenceThreshold).
    /// Pre-seleccionados para confirmación en batch desde UI.
    /// Todavía no son verdad financiera — requieren confirmación explícita.
    /// </summary>
    public required IReadOnlyList<MatchedPair> AutoConfirmable { get; init; }

    /// <summary>
    /// Confianza media (score ≥ MediumConfidenceThreshold, &lt; High).
    /// Requieren revisión manual uno a uno.
    /// </summary>
    public required IReadOnlyList<MatchedPair> NeedsReview { get; init; }

    /// <summary>
    /// Confianza baja. No se muestran en UI por defecto.
    /// Disponibles para diagnóstico del motor.
    /// </summary>
    public required IReadOnlyList<MatchedPair> Ignored { get; init; }

    /// <summary>
    /// Movimientos de referencia sin candidato encontrado.
    /// Incluye near-misses para diagnóstico.
    /// </summary>
    public required IReadOnlyList<UnmatchedMovement> Unmatched { get; init; }

    /// <summary>
    /// Grupos detectados como posibles duplicados o splits.
    /// </summary>
    public required IReadOnlyList<SuspiciousGroup> Suspicious { get; init; }

    /// <summary>Métricas del run.</summary>
    public required ReconciliationSummary Summary { get; init; }

    public required TimeSpan Elapsed { get; init; }

    // ── Totales de conveniencia ───────────────────────────────────

    public int TotalSuggestions => AutoConfirmable.Count + NeedsReview.Count;
    public int TotalIgnored => Ignored.Count;

    // ── Factory ───────────────────────────────────────────────────

    /// <summary>
    /// Construye ReconciliationSuggestions desde el ReconciliationResult del motor.
    /// Clasifica los MatchedPairs según su MatchConfidence.
    /// </summary>
    internal static ReconciliationSuggestions FromResult(
        ReconciliationResult result,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        var autoConfirmable = new List<MatchedPair>();
        var needsReview = new List<MatchedPair>();
        var ignored = new List<MatchedPair>();

        foreach (var pair in result.Matched)
        {
            switch (pair.Confidence)
            {
                case MatchConfidence.High:
                    autoConfirmable.Add(pair);
                    break;
                case MatchConfidence.Medium:
                    needsReview.Add(pair);
                    break;
                default: // Low, None
                    ignored.Add(pair);
                    break;
            }
        }

        return new ReconciliationSuggestions
        {
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            AutoConfirmable = autoConfirmable.AsReadOnly(),
            NeedsReview = needsReview.AsReadOnly(),
            Ignored = ignored.AsReadOnly(),
            Unmatched = result.Unmatched,
            Suspicious = result.Suspicious,
            Summary = result.Summary,
            Elapsed = result.Elapsed,
        };
    }
}