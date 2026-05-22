namespace FinancialSystem.Application.Reconciliation
{
    /// <summary>
    /// Parámetros que controlan las heurísticas de matching.
    /// Todos tienen defaults razonables para el caso argentino.
    /// </summary>
    public sealed class ReconciliationOptions
    {
        public const string SectionName = "Reconciliation";

        // ── Umbrales de confianza ────────────────────────────────────
        /// <summary>Score mínimo para considerar un match de alta confianza.</summary>
        public double HighConfidenceThreshold { get; set; } = 0.85;

        /// <summary>Score mínimo para considerar un match de confianza media.</summary>
        public double MediumConfidenceThreshold { get; set; } = 0.60;

        /// <summary>Score mínimo para reportar como "near miss" (no matchea pero estuvo cerca).</summary>
        public double NearMissThreshold { get; set; } = 0.35;

        // ── Tolerancias de monto ─────────────────────────────────────
        /// <summary>
        /// Diferencia máxima absoluta en ARS para considerar montos iguales.
        /// Default: 50 ARS — cubre redondeos "Farmacia = 4700, banco = 4660".
        /// </summary>
        public decimal AmountAbsoluteTolerance { get; set; } = 50m;

        /// <summary>
        /// Diferencia máxima relativa. Default: 2% — para montos grandes.
        /// El motor usa max(absoluta, relativa) para dar más flexibilidad.
        /// </summary>
        public double AmountRelativeTolerance { get; set; } = 0.02;

        // ── Tolerancias de fecha ─────────────────────────────────────
        /// <summary>
        /// Ventana de días para considerar fechas "cercanas".
        /// Default: 3 días — cubre demoras de acreditación bancaria.
        /// </summary>
        public int DateWindowDays { get; set; } = 3;

        // ── Pesos de reglas ──────────────────────────────────────────
        // Los pesos se normalizan automáticamente por IMatchScorer,
        // por lo que sólo importan en proporción relativa.
        public double AmountRuleWeight { get; set; } = 0.45;
        public double DateRuleWeight { get; set; } = 0.25;
        public double DescriptionRuleWeight { get; set; } = 0.20;
        public double PaymentMethodRuleWeight { get; set; } = 0.10;

        // ── Detección de duplicados ──────────────────────────────────
        /// <summary>
        /// Ventana de días para buscar posibles duplicados dentro de la misma fuente.
        /// </summary>
        public int DuplicateDetectionWindowDays { get; set; } = 1;

        /// <summary>
        /// Diferencia máxima de monto para sospechar duplicado.
        /// </summary>
        public decimal DuplicateAmountTolerance { get; set; } = 1m;

        // ── Fuzzy description matching ───────────────────────────────
        /// <summary>
        /// Score mínimo de similitud de descripción para aportar puntaje.
        /// Por debajo de este valor se considera sin relación.
        /// </summary>
        public double DescriptionMinimumSimilarity { get; set; } = 0.25;
    }
}
