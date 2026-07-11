using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Application.Suggestions;

/// <summary>
/// Las recomendaciones disponibles para un único movimiento — de 0 a N
/// <see cref="ClassificationSuggestion"/>, a lo sumo una por <see cref="SuggestionDimension"/>.
/// <see cref="SourceEntityType"/>+<see cref="SourceId"/> identifican el movimiento con la
/// misma identidad que ya usa <c>FinancialMovement.SourceId</c> — no un esquema nuevo.
///
/// Una lista vacía en <see cref="Suggestions"/> es un resultado válido y esperado: significa
/// que el motor no encontró señal suficiente para ese movimiento (ej. sin historial con
/// la misma descripción exacta, ver <c>ClassificationSuggestionService</c>).
/// </summary>
/// <param name="SourceEntityType">Misma fuente que <c>FinancialMovement.Source</c> identifica vía <c>SourceId</c>.</param>
/// <param name="SourceId">Id real del movimiento en su tabla de origen (<c>FinancialMovement.SourceId</c>).</param>
/// <param name="Suggestions">A lo sumo una recomendación por <see cref="SuggestionDimension"/>.</param>
public sealed record ClassificationSuggestionSet(
    SourceEntityType SourceEntityType,
    Guid SourceId,
    IReadOnlyList<ClassificationSuggestion> Suggestions);
