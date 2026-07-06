namespace FinancialSystem.Domain.Enums;

/// <summary>
/// Rol semántico de un ítem dentro de un movimiento clasificado.
/// </summary>
public enum MovementRole
{
    /// <summary>
    /// Movimiento de verdad contable: banco o tarjeta.
    /// Es la fuente de verdad financiera del sistema.
    /// </summary>
    Reference = 1,

    /// <summary>
    /// Movimiento auxiliar usado como coincidencia durante la clasificación.
    /// Hoy: registros legacy importados desde Excel.
    /// A futuro: podría ser cualquier fuente auxiliar externa.
    /// </summary>
    Candidate = 2,
}