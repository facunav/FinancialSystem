using FinancialSystem.Domain.Review;

namespace FinancialSystem.Application.Review;

/// <summary>
/// Orquesta <see cref="IMovementLoader"/> e <see cref="ISuspicionDetector"/> para
/// producir el <see cref="ReviewResult"/> completo de un período: los movimientos
/// encontrados y los grupos sospechosos detectados entre ellos.
///
/// PUNTO DE EXTENSIÓN: este es el lugar donde debería integrarse un futuro motor de
/// recomendaciones (historial de clasificaciones, reglas, IA) — como un componente
/// más orquestado acá, siguiendo el mismo patrón de composición que ya usa
/// ISuspicionDetector. Ver el comentario en ReviewResult.cs para la razón por la que
/// esa forma no se diseña todavía.
/// </summary>
public interface IReviewEngine
{
    Task<ReviewResult> GenerateAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
