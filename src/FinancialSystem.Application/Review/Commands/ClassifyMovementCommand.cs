using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Application.Review.Commands;

/// <summary>
/// Clasifica manualmente un movimiento crudo sin coincidencia externa
/// (crea un ClassifiedMovement con Status=Reviewed).
///
/// EffectiveDate es opcional. Al crear un ClassifiedMovement nuevo: si viene, se usa
/// como período financiero inicial; si no, nace igual a la fecha bancaria del
/// movimiento. Al reclasificar uno existente: null significa "no tocar el período
/// financiero actual" — nunca se recalcula ni se resetea (ver ClassifyMovementHandler).
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
