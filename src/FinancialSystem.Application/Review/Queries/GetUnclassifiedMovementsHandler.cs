using FinancialSystem.Domain.Review;

namespace FinancialSystem.Application.Review.Queries;

/// <summary>
/// Expone el resultado de <see cref="IReviewEngine"/> para el período pedido.
/// Sin lógica propia: es un pasamano directo hacia el motor. Validar el rango
/// de fechas es responsabilidad del caller (endpoint).
/// </summary>
public sealed class GetUnclassifiedMovementsHandler
{
    private readonly IReviewEngine _reviewEngine;

    public GetUnclassifiedMovementsHandler(IReviewEngine reviewEngine) => _reviewEngine = reviewEngine;

    public Task<ReviewResult> Handle(
        GetUnclassifiedMovementsQuery query, CancellationToken cancellationToken = default) =>
        _reviewEngine.GenerateAsync(query.From, query.To, cancellationToken);
}
