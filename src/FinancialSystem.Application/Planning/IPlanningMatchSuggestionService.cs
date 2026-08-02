namespace FinancialSystem.Application.Planning;

/// <summary>
/// Una coincidencia candidata entre un PlanningItem pendiente y un movimiento real ya
/// clasificado -- ver docs/Epics/Epica-PlanificacionMensual.md, sección 9
/// ("Integración futura"). Es una propuesta de lectura, nunca una escritura: el único
/// efecto posible es que el usuario decida, manualmente, marcar el PlanningItem como
/// pagado (mismo comando ya existente, MarkPlanningItemAsPaidCommand) -- esta clase no
/// lo hace por él.
/// </summary>
public sealed record PlanningItemMatchSuggestion(
    Guid PlanningItemId,
    string PlanningItemTitle,
    Guid ClassifiedMovementId,
    string MovementDescription,
    decimal MovementAmount,
    DateTime MovementEffectiveDate);

/// <summary>
/// Detecta, sin persistir nada, posibles coincidencias entre los PlanningItem
/// pendientes de un mes y los movimientos ya clasificados de ese mismo mes -- sección
/// 9 de la épica. Deliberadamente ingenuo (comparación de texto, sin IA, sin
/// puntuación de confianza): el mismo criterio "exact match / substring, nada de fuzzy"
/// que ya usa ClassificationSuggestionService.Normalize (Infrastructure/Suggestions),
/// reimplementado acá en vez de reutilizado para no tocar ese módulo (fuera de alcance
/// de este patch).
/// </summary>
public interface IPlanningMatchSuggestionService
{
    /// <summary>
    /// Sugerencias para el mes indicado. Null si el PlanningMonth no existe; lista
    /// vacía si existe pero no hay PlanningItem pendientes o ningún movimiento coincide.
    /// </summary>
    Task<IReadOnlyList<PlanningItemMatchSuggestion>?> GetSuggestionsAsync(
        Guid planningMonthId, CancellationToken ct = default);
}
