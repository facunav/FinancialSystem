namespace FinancialSystem.Domain.Review;

// RESULTADO DEL PROCESO DE REVISIÓN
// PR-L4: hasta acá este resultado también incluía sugerencias de matching contra
// movimientos "Candidate" (legacy Excel) — Matched/Unmatched/MatchScore/MatchConfidence/
// RuleContribution/ReviewSummary. Ese mecanismo se retiró completo (ver Épica K, limpieza
// de soporte Legacy): no queda ninguna segunda fuente contra la que reconciliar banco/
// tarjeta. Lo que sigue vigente es lo que nunca dependió de esa segunda fuente: la lista
// de movimientos del período y los grupos sospechosos (duplicados/splits dentro de la
// misma lista, ver ISuspicionDetector).
//
// PUNTO DE EXTENSIÓN: IReviewEngine/ReviewResult siguen siendo el lugar donde un futuro
// motor de recomendaciones (historial de clasificaciones, reglas, IA) debería integrarse
// — orquestado igual que ISuspicionDetector hoy, como un componente más inyectado en
// ReviewEngine. Su resultado NO debería modelarse como un nuevo MatchedPair/candidato a
// emparejar: una recomendación de clasificación (categoría/tipo/impacto sugeridos) es un
// tipo de dato distinto a "acá hay un movimiento parecido en otra fuente". Diseñar esa
// forma ahora, sin una segunda implementación real que la valide, sería la abstracción
// prematura que este proyecto evita — se define cuando ese motor exista de verdad.

/// <summary>
/// El resultado completo de una sesión de revisión para un período: los movimientos
/// encontrados y los grupos sospechosos detectados entre ellos.
/// </summary>
public sealed record ReviewResult
{
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required IReadOnlyList<FinancialMovement> Movements { get; init; }
    public required IReadOnlyList<SuspiciousGroup> Suspicious { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
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

public enum SuspicionReason
{
    PossibleDuplicate,     // Mismo monto, misma fecha, misma fuente
    SplitTransaction,      // Varios movimientos suman al total de otro
    RoundingAnomaly,       // Diferencia exactamente igual en múltiples pares
}
