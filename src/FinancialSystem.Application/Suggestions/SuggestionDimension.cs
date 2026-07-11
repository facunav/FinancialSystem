namespace FinancialSystem.Application.Suggestions;

/// <summary>
/// Qué dimensión de <c>ClassifiedMovement</c> recomienda un <see cref="ClassificationSuggestion"/>.
///
/// Las 4 dimensiones de clasificación son independientes entre sí (ver
/// <c>ClassifiedMovement.cs</c>) — un motor de sugerencias puede tener certeza sobre
/// una y ninguna señal sobre las demás. Por eso cada <see cref="ClassificationSuggestion"/>
/// recomienda una sola dimensión, no las 4 juntas con una confianza global forzada.
///
/// Si en el futuro el dominio agrega una quinta dimensión clasificable, se agrega un
/// valor acá — no un campo nuevo suelto en <see cref="ClassificationSuggestion"/>.
/// </summary>
public enum SuggestionDimension
{
    Category = 1,
    MovementType = 2,
    FinancialImpact = 3,
    Counterparty = 4,
}
