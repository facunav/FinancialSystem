namespace FinancialSystem.Application.Suggestions;

/// <summary>
/// Una recomendación para una sola dimensión de clasificación (ver
/// <see cref="SuggestionDimension"/>) de un movimiento — nunca una comparación contra
/// otro movimiento, nunca un candidato a emparejar. Diseñado desde cero para PR-S1/PR-S2:
/// no reutiliza ninguna forma del extinto <c>MovementSuggestion</c> (motor de matching
/// Legacy, retirado en PR-L4), que representaba un movimiento candidato encontrado en
/// otra fuente, no una recomendación de clasificación.
///
/// El usuario no "confirma" un <see cref="ClassificationSuggestion"/> con un comando
/// dedicado: la sugerencia solo pre-completa valores en el mismo
/// <c>ClassifyMovementCommand</c> que ya existe. No hay una acción propia a modelar acá.
/// </summary>
/// <param name="Dimension">Qué dimensión de <c>ClassifiedMovement</c> se sugiere.</param>
/// <param name="Value">
/// El valor sugerido para <see cref="Dimension"/>. Su tipo real depende de la dimensión:
/// <see cref="SuggestionDimension.Category"/> y <see cref="SuggestionDimension.Counterparty"/>
/// llevan un <see cref="Guid"/> (el <c>Id</c> de la <c>Category</c>/<c>Counterparty</c>
/// sugerida); <see cref="SuggestionDimension.MovementType"/> lleva un
/// <c>FinancialSystem.Domain.Enums.MovementType</c>; <see cref="SuggestionDimension.FinancialImpact"/>
/// lleva un <c>FinancialSystem.Domain.Enums.FinancialImpact</c>. Un consumidor debe
/// inspeccionar <see cref="Dimension"/> antes de castear <see cref="Value"/>.
/// </param>
/// <param name="Confidence">Qué tan confiable es esta recomendación, en escala ordinal.</param>
/// <param name="Reason">
/// Motivo legible para mostrar en la UI (ej. "Clasificaste así 8 de las últimas 9 veces
/// con esta contraparte"). No es opcional: una sugerencia sin motivo visible es una caja
/// negra que el usuario no tiene por qué confiar.
/// </param>
public sealed record ClassificationSuggestion(
    SuggestionDimension Dimension,
    object Value,
    SuggestionConfidence Confidence,
    string Reason);
