using FinancialSystem.Application.Suggestions;
using FinancialSystem.Domain.Review;

namespace FinancialSystem.Infrastructure.Suggestions;

/// <summary>
/// Implementación nula de <see cref="IClassificationSuggestionService"/>: siempre
/// responde "sin sugerencias", sin consultar la base, sin reglas, sin IA.
///
/// PR-S2: es la única implementación de este PR — introduce la infraestructura del
/// motor de sugerencias sin cambiar el comportamiento del sistema. Nada la consume
/// todavía (ver DI); cuando un futuro PR conecte un consumidor real, este servicio
/// garantiza que el sistema se comporta exactamente igual que hoy hasta que se
/// reemplace por una implementación con señal real.
/// </summary>
internal sealed class NullClassificationSuggestionService : IClassificationSuggestionService
{
    public Task<IReadOnlyList<ClassificationSuggestionSet>> SuggestAsync(
        IReadOnlyList<FinancialMovement> movements, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ClassificationSuggestionSet>>([]);
}
