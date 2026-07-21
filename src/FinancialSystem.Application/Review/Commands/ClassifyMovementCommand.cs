using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Application.Review.Commands;

/// <summary>
/// Clasifica manualmente un movimiento crudo sin coincidencia externa
/// (crea un ClassifiedMovement con Status=Reviewed).
///
/// EffectiveDate es opcional y solo tiene efecto al reclasificar un movimiento ya
/// existente: null significa "no tocar el período financiero actual". En la creación
/// de un ClassifiedMovement nuevo se ignora — EffectiveDate siempre nace igual a la
/// fecha bancaria del movimiento (ver ClassifyMovementHandler).
/// </summary>
public sealed record ClassifyMovementCommand(
    SourceEntityType SourceEntityType,
    Guid SourceId,
    Guid CategoryId,
    MovementType MovementType,
    FinancialImpact FinancialImpact,
    Guid? CounterpartyId,
    string? Comment,
    DateTime? EffectiveDate = null);
