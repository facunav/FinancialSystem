using FinancialSystem.Domain.Review;

namespace FinancialSystem.Application.Review;

/// <summary>
/// Orquesta <see cref="IMovementLoader"/>, <see cref="IMatchScorer"/> e
/// <see cref="ISuspicionDetector"/> para producir el <see cref="ReviewResult"/>
/// completo de un período: sugerencias de matching, movimientos sin sugerencia
/// y grupos sospechosos.
/// </summary>
public interface IReviewEngine
{
    Task<ReviewResult> GenerateAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
