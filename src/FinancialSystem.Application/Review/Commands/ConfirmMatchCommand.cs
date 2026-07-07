using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Application.Review.Commands;

/// <summary>Un movimiento crudo participante en la confirmación, con su rol dentro del grupo.</summary>
public sealed record ConfirmMatchItem(SourceEntityType SourceEntityType, Guid SourceId, MovementRole Role);

/// <summary>
/// Confirma una sugerencia de matching (1↔1, N↔1, 1↔N o N↔M): crea un
/// ClassifiedMovement (Status=Confirmed) con un ClassifiedMovementItem por
/// cada item del grupo.
/// </summary>
public sealed record ConfirmMatchCommand(
    IReadOnlyList<ConfirmMatchItem> Items,
    Guid CategoryId,
    MovementType MovementType,
    FinancialImpact FinancialImpact,
    Guid? CounterpartyId);
