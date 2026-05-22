namespace FinancialSystem.Domain.Reconciliation;

// ════════════════════════════════════════════════════════════════════
// RESULTADO DE CONCILIACIÓN
//
// Jerarquía de tipos que representa el output del motor de conciliación.
// Cada tipo de resultado tiene semántica clara y no ambigua.
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// El resultado completo de una sesión de conciliación para un período.
/// </summary>
public sealed record ReconciliationResult
{
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required IReadOnlyList<MatchedPair> Matched { get; init; }
    public required IReadOnlyList<UnmatchedMovement> Unmatched { get; init; }
    public required IReadOnlyList<SuspiciousGroup> Suspicious { get; init; }
    public required ReconciliationSummary Summary { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Par de movimientos conciliados entre sí, con score y evidencia.
/// </summary>
public sealed record MatchedPair
{
    public required FinancialMovement Reference { get; init; }   // movimiento banco/tarjeta
    public required FinancialMovement Candidate { get; init; }   // movimiento manual
    public required MatchScore Score { get; init; }
    public required MatchConfidence Confidence { get; init; }

    /// <summary>Qué reglas contribuyeron a este match y cuánto.</summary>
    public required IReadOnlyList<RuleContribution> Contributions { get; init; }

    /// <summary>Diferencia de monto entre los dos movimientos.</summary>
    public decimal AmountDelta => Math.Abs(Reference.Amount - Candidate.Amount);

    /// <summary>Diferencia de días entre las fechas.</summary>
    public int DateDeltaDays => Math.Abs((Reference.Date - Candidate.Date).Days);
}

/// <summary>
/// Movimiento sin contraparte encontrada.
/// </summary>
public sealed record UnmatchedMovement
{
    public required FinancialMovement Movement { get; init; }
    public required UnmatchedReason Reason { get; init; }

    /// <summary>
    /// Candidatos cercanos que no llegaron al threshold de confianza.
    /// Útil para debugging y para mejorar matching futuro.
    /// </summary>
    public IReadOnlyList<MatchedPair> NearMisses { get; init; } = [];
}

/// <summary>
/// Grupo de movimientos sospechosos de ser duplicados entre sí.
/// </summary>
public sealed record SuspiciousGroup
{
    public required IReadOnlyList<FinancialMovement> Movements { get; init; }
    public required SuspicionReason Reason { get; init; }
    public required string Description { get; init; }
}

/// <summary>
/// Score compuesto resultado de aplicar todas las reglas de matching.
/// Cada componente está en [0.0, 1.0]. El total es la suma ponderada.
/// </summary>
public sealed record MatchScore
{
    public required double AmountScore { get; init; }
    public required double DateScore { get; init; }
    public required double DescriptionScore { get; init; }
    public required double PaymentMethodScore { get; init; }

    /// <summary>Score total ponderado. En [0.0, 1.0].</summary>
    public required double Total { get; init; }

    public override string ToString() =>
        $"Total={Total:P0} [Monto={AmountScore:P0}, Fecha={DateScore:P0}, Desc={DescriptionScore:P0}, Pago={PaymentMethodScore:P0}]";
}

/// <summary>Contribución de una regla individual al score final.</summary>
public sealed record RuleContribution(
    string RuleName,
    double Score,
    double Weight,
    string? Detail = null);

/// <summary>
/// Nivel de confianza derivado del score total.
/// Los umbrales son configurables via ReconciliationOptions.
/// </summary>
public enum MatchConfidence
{
    /// <summary>Score ≥ 0.85: match automático seguro.</summary>
    High,

    /// <summary>Score ≥ 0.60: match probable, requiere revisión.</summary>
    Medium,

    /// <summary>Score ≥ 0.40: posible match, alta incertidumbre.</summary>
    Low,

    /// <summary>Score < 0.40: no considerado match.</summary>
    None,
}

public enum UnmatchedReason
{
    NoCandidate,           // No existe ningún movimiento parecido
    BelowThreshold,        // Candidatos existen pero el score es insuficiente
    ConflictingMatch,      // El mejor candidato ya fue asignado a otro movimiento
    CurrencyMismatch,      // Monedas incompatibles
    OutOfPeriod,           // Fuera del período analizado
}

public enum SuspicionReason
{
    PossibleDuplicate,     // Mismo monto, misma fecha, misma fuente
    SplitTransaction,      // Varios movimientos suman al total de otro
    RoundingAnomaly,       // Diferencia exactamente igual en múltiples pares
}

/// <summary>Métricas agregadas de la conciliación.</summary>
public sealed record ReconciliationSummary
{
    public int TotalReferenceMovements { get; init; }
    public int TotalCandidateMovements { get; init; }
    public int HighConfidenceMatches { get; init; }
    public int MediumConfidenceMatches { get; init; }
    public int LowConfidenceMatches { get; init; }
    public int UnmatchedReference { get; init; }
    public int UnmatchedCandidate { get; init; }
    public int SuspiciousGroups { get; init; }

    public int TotalMatched => HighConfidenceMatches + MediumConfidenceMatches + LowConfidenceMatches;

    public double ReconciliationRate =>
        TotalReferenceMovements == 0
            ? 0
            : (double)TotalMatched / TotalReferenceMovements;

    public decimal TotalUnmatchedAmount { get; init; }

    public override string ToString() =>
        $"Conciliados: {TotalMatched}/{TotalReferenceMovements} ({ReconciliationRate:P0}) | " +
        $"Sin match: ref={UnmatchedReference} cand={UnmatchedCandidate} | " +
        $"Sospechosos: {SuspiciousGroups}";
}
