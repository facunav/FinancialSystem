using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Application.Audit.Commands;

/// <summary>
/// Identifica un movimiento con la misma convención que ya usa el resto del proyecto:
/// SourceEntityType + SourceId (ver InvestigationReference, ClassifiedMovementItem).
/// </summary>
public sealed record MovementKey(SourceEntityType SourceEntityType, Guid SourceId);

/// <summary>
/// Registra que una o más movimientos fueron revisados y su clasificación actual se
/// mantiene tal cual está — ver MovementAuditDecision. Acepta una lista para no necesitar un
/// endpoint separado para revisión en lote (ej. aceptar de una vez todos los movimientos
/// de un mismo grupo en el Centro de Auditoría); una lista de un solo elemento cubre el
/// caso de revisar un único movimiento.
/// </summary>
public sealed record ReviewMovementsCommand(IReadOnlyList<MovementKey> Movements);
