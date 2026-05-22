using FinancialSystem.Domain.Entities;

namespace FinancialSystem.Application.Reconciliation;

/// <summary>
/// Contrato para leer gastos manuales desde la DB para conciliación.
///
/// Vive en Application porque es el motor de conciliación (Application)
/// quien lo consume. La implementación vive en Infrastructure.
///
/// Sólo lectura: la conciliación nunca escribe ManualExpenses.
/// </summary>
public interface IManualExpenseRepository
{
    /// <summary>
    /// Devuelve todos los gastos manuales dentro del período, ambos extremos inclusivos.
    /// Filtra por fecha UTC. Si sheet es null, devuelve ambas hojas.
    /// </summary>
    Task<IReadOnlyList<ManualExpense>> GetByPeriodAsync(
        DateOnly from,
        DateOnly to,
        ManualExpenseSheet? sheet = null,
        CancellationToken ct = default);
}
