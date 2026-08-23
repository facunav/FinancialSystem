namespace FinancialSystem.Domain.Dedupe;

/// <summary>
/// Rol de una representación física dentro de un grupo de identidad de movimiento
/// (ver especificación DEDUPE-003-CONV, sección A: REPRESENTACIÓN FÍSICA vs.
/// MOVIMIENTO REAL vs. IDENTIDAD DE MOVIMIENTO).
/// </summary>
public enum IdentityRole
{
    /// <summary>La representación con forma "Nro:" — el ancla del par.</summary>
    Pendiente = 0,

    /// <summary>La representación liquidada/transformada, con o sin número sobreviviente.</summary>
    Liquidado = 1,

    /// <summary>
    /// Copia física adicional del Liquidado, repetida en una exportación acumulativa
    /// posterior con firma idéntica (Fecha + Concepto normalizado) — DEDUPE-003-CONV
    /// sección I. Pertenece al mismo IdentityGroupId que el Liquidado original.
    /// </summary>
    CarryForward = 2,
}
