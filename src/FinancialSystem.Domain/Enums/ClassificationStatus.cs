namespace FinancialSystem.Domain.Enums;

/// <summary>
/// Estado de un movimiento clasificado.
/// Solo existen dos estados finales: Confirmed y Reviewed.
/// Las sugerencias del motor son efímeras (no persistidas como entidad propia hoy).
/// Todo ClassifiedMovement representa verdad financiera verificada, sin importar
/// si llegó ahí con o sin coincidencia externa.
/// </summary>
public enum ClassificationStatus
{
    /// <summary>
    /// El usuario aceptó una coincidencia con un movimiento externo (legacy/manual).
    /// Puede ser 1↔1, N↔1, 1↔N, N↔M.
    /// </summary>
    Confirmed = 1,

    /// <summary>
    /// El usuario clasificó el movimiento manualmente, sin coincidencia externa.
    /// Requiere las 4 dimensiones de clasificación igual que un Confirmed.
    /// Ejemplos: transferencias, comisiones, gastos de tarjeta sin Excel.
    /// </summary>
    Reviewed = 2,
}