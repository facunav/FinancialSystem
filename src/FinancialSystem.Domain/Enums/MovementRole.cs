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
    /// PR-L4/PR-L5: el único productor (ConfirmMatchCommand, sobre registros legacy
    /// importados desde Excel) se eliminó junto con LegacyImportedExpense — este valor
    /// ya no se genera. Se conserva (mismo número) porque filas históricas de
    /// ClassifiedMovementItem lo usan y deben seguir siendo válidas.
    /// </summary>
    Candidate = 2,
}