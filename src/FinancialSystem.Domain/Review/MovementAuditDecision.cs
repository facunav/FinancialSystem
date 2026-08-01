using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Domain.Review;

/// <summary>
/// Registro de que una persona revisó un movimiento marcado por el Centro de Auditoría
/// como potencialmente mal clasificado, y decidió mantener la clasificación actual tal
/// cual está. No modifica ClassifiedMovement, no participa del historial que usa
/// ClassificationSuggestionService para aprender, y no oculta el hallazgo: es un hecho
/// independiente sobre la revisión humana, no una corrección de datos ni una supresión
/// del problema detectado.
///
/// La existencia del registro ES el estado: no hay un campo Status. Si existe una fila
/// para (SourceEntityType, SourceId), ese movimiento fue revisado; si no existe, sigue
/// pendiente.
///
/// SourceEntityType + SourceId identifican el movimiento con la misma convención que ya
/// usan ClassificationSuggestionSet/InvestigationReference/ClassifiedMovementItem — no
/// alcanza con SourceId solo: BankStatement y Transaction son tablas de origen distintas
/// (ver ClassificationSuggestionService.ToSourceEntityType), sin garantía de unicidad
/// entre ambas.
///
/// NOMBRE: no "MovementReview" -- ese nombre ya está tomado por el flujo de
/// clasificación existente (MovementReviewEndpoints, /api/movement-review/classify,
/// namespace FinancialSystem.Application.Review.Commands) y significa algo distinto
/// ahí (clasificar un movimiento). "MovementAuditDecision" evita la colisión de
/// vocabulario con ese concepto ya existente.
/// </summary>
public class MovementAuditDecision
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Qué tabla contiene el movimiento revisado.</summary>
    public SourceEntityType SourceEntityType { get; set; }

    /// <summary>Id del movimiento en su tabla de origen.</summary>
    public Guid SourceId { get; set; }

    public DateTime ReviewedAtUtc { get; set; }
}
