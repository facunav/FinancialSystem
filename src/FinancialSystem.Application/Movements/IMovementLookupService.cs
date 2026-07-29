using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Application.Movements;

// ── Modelo de resultado ────────────────────────────────────────────────────────
// Distinto de MovementView (pensado para listar un período, con forma de UI):
// esto es la vista completa de UN movimiento identificado por SourceEntityType +
// SourceId -- misma convención que ya usan ClassifiedMovementItem y
// ClassifyMovementCommand, no una identificación nueva. Pensado para
// investigación: no oculta campos técnicos (RawLine, ExternalId, etc.) que
// MovementView deliberadamente no expone.

/// <summary>
/// Detalle completo de un movimiento de origen (Transaction o BankStatement), con
/// su clasificación si ya la tiene. Los campos técnicos que no aplican al
/// SourceEntityType real quedan en null (ej. CouponNumber en un BankStatement).
/// </summary>
public sealed record MovementDetail(
    SourceEntityType SourceEntityType,
    Guid SourceId,
    DateTime Date,
    string Description,
    decimal Amount,
    string Currency,
    string? SourceFile,
    Guid? FinancialAccountId,
    string? FinancialAccountName,
    string? ExternalId,
    string? CouponNumber,
    string? RawLine,
    string? BankName,
    string? AccountNumber,
    string? BankDetail,
    decimal? Balance,
    string? SheetName,
    int? RowNumber,
    string? Merchant,
    DateTime? MerchantAtUtc,
    DateTime SourceRecordedAtUtc,
    MovementClassificationDetail? Classification);

/// <summary>
/// Clasificación (ClassifiedMovement) de un movimiento, junto con el grupo de
/// matching al que pertenece su ClassifiedMovementItem. GroupItems tiene un único
/// elemento en el caso normal (clasificación 1:1); más de uno solo en grupos N↔M
/// históricos (ver ClassifiedMovementItem — ConfirmMatchCommand, el único
/// productor de grupos nuevos, se retiró en PR-L4).
/// </summary>
public sealed record MovementClassificationDetail(
    Guid ClassifiedMovementId,
    DateTime EffectiveDate,
    Guid CategoryId,
    string? CategoryName,
    Guid? CounterpartyId,
    string? CounterpartyName,
    MovementType MovementType,
    FinancialImpact FinancialImpact,
    ClassificationStatus Status,
    ProcessingSource ProcessingSource,
    string? Comment,
    double? MatchScore,
    decimal? AmountDelta,
    DateTime CreatedAt,
    DateTime ProcessedAt,
    string? ProcessedBy,
    MovementRole ItemRole,
    IReadOnlyList<MatchGroupItem> GroupItems);

/// <summary>Un ítem del grupo de matching de un ClassifiedMovement.</summary>
public sealed record MatchGroupItem(
    SourceEntityType SourceEntityType,
    Guid SourceId,
    MovementRole Role);

// ── Interfaz del servicio ─────────────────────────────────────────────────────

/// <summary>
/// Lectura puntual de un movimiento por SourceEntityType + SourceId, con toda la
/// información de investigación disponible (dato crudo, clasificación, grupo de
/// matching). A diferencia de IMovementsQueryService (rango de fechas, forma de
/// UI), esto es lookup por id — no existía ninguna pieza reutilizable para esto:
/// ClassifyMovementHandler.FindSourceAsync es privado y devuelve un snapshot
/// mínimo pensado para el flujo de escritura (clasificar), no para investigación.
/// Nunca escribe nada.
/// </summary>
public interface IMovementLookupService
{
    Task<MovementDetail?> GetBySourceAsync(
        SourceEntityType sourceEntityType,
        Guid sourceId,
        CancellationToken cancellationToken = default);
}
