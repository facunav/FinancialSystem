using FinancialSystem.Domain.Review;

namespace FinancialSystem.Application.Review;

/// <summary>
/// Carga los movimientos financieros crudos de un período y los adapta al modelo
/// neutro <see cref="FinancialMovement"/> sobre el que opera el motor de sugerencias.
/// </summary>
public interface IMovementLoader
{
    Task<IReadOnlyList<FinancialMovement>> LoadAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
