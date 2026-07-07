using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Application.Review.Commands;

/// <summary>
/// Clasifica manualmente un movimiento crudo sin coincidencia externa
/// (crea un ClassifiedMovement con Status=Reviewed).
/// </summary>
public sealed record ClassifyMovementCommand(
    SourceEntityType SourceEntityType,
    Guid SourceId,
    Guid CategoryId,
    MovementType MovementType,
    FinancialImpact FinancialImpact,
    Guid? CounterpartyId,
    string? Comment);
