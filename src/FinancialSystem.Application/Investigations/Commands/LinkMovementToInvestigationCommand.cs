using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Application.Investigations.Commands;

/// <summary>
/// Asocia un movimiento existente a una Investigation, sin crear hallazgos, comentarios
/// ni historial — ver ADR-007 §3 ("Referencia"). Identifica el movimiento con la misma
/// convención que ya usa el resto del proyecto: SourceEntityType + SourceId (ver
/// InvestigationReference, ClassifiedMovementItem, GetMovement/ExplainMovement).
/// </summary>
public sealed record LinkMovementToInvestigationCommand(
    Guid InvestigationId,
    SourceEntityType SourceEntityType,
    Guid SourceId);
