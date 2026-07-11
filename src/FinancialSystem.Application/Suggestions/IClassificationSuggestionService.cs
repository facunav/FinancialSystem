using FinancialSystem.Domain.Review;

namespace FinancialSystem.Application.Suggestions;

/// <summary>
/// Recomienda valores de clasificación (categoría, tipo, impacto financiero, contraparte)
/// para movimientos pendientes, basándose en el historial de clasificaciones ya hechas por
/// el usuario — nunca comparando un movimiento contra otro movimiento (esa es la
/// responsabilidad, no relacionada, de <c>ISuspicionDetector</c>).
///
/// PR-S1 (ver docs/Architecture/PRS1analisismotorsugerencias.md): deliberadamente
/// desacoplado de <c>IReviewEngine</c> — este servicio opera por movimiento(s), no por
/// período completo, y su ciclo de vida (reglas → historial → IA, ver roadmap del
/// análisis) es independiente del de carga+detección de sospechosos. Se invoca
/// directamente desde quien lo necesite (ej. <c>IMovementsQueryService</c>), no orquestado
/// por <c>IReviewEngine</c>.
///
/// PR-S2 introdujo el contrato solo con una implementación nula (sin historial, sin IA).
/// PR-S3 agrega la primera implementación real (<c>ClassificationSuggestionService</c>):
/// exact match de descripción normalizada contra <c>ClassifiedMovement</c>. PR-S7 agrega
/// una segunda heurística a esa misma implementación (enriquecimiento vía
/// <c>Counterparty.Default*</c>) sin tocar este contrato. Reglas configurables y
/// proveedores de IA pueden llegar en PRs posteriores, también sin necesidad de
/// cambiarlo.
/// </summary>
public interface IClassificationSuggestionService
{
    /// <summary>
    /// Recomienda valores de clasificación para un lote de movimientos pendientes.
    ///
    /// Deliberadamente por lote y no <c>SuggestAsync(FinancialMovement)</c> uno a la vez:
    /// un consumidor típico (la pantalla Movimientos) necesita sugerencias para todos los
    /// pendientes de un período de una sola vez — una implementación real que consulte
    /// historial puede resolver eso con una sola consulta agregada en vez de N consultas,
    /// el mismo criterio que ya sigue <c>MovementsQueryService.LoadClassifiedAsync</c> al
    /// resolver cuentas asignadas en bloque. El resultado no incluye una entrada por cada
    /// movimiento sin sugerencia — ausencia en la lista equivale a "sin sugerencias" para
    /// ese movimiento, mismo patrón que ya usa <c>ReviewResult.Suspicious</c>.
    /// </summary>
    Task<IReadOnlyList<ClassificationSuggestionSet>> SuggestAsync(
        IReadOnlyList<FinancialMovement> movements,
        CancellationToken cancellationToken = default);
}
