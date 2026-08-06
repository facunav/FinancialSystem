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

    /// <summary>
    /// Versión en lote de <see cref="GetBySourceAsync"/> (PATCH-044): resuelve varios
    /// movimientos de una sola vez, con una cantidad fija de consultas a la base --
    /// no una tanda de consultas por cada elemento de <paramref name="sources"/>. Pensada
    /// para consumidores que hoy resuelven varios orígenes en un
    /// <c>foreach</c> + <c>await</c> individual (ver
    /// <c>InvestigationTools.BuildInvestigationContextAsync</c>).
    ///
    /// Elementos inexistentes (un <c>SourceId</c> que no resuelve ninguna fila real,
    /// sea porque nunca existió o porque fue eliminado) simplemente no aparecen como
    /// clave en el diccionario devuelto -- mismo criterio que <see cref="GetBySourceAsync"/>,
    /// que devuelve <c>null</c> para esos casos; acá "ausente del diccionario" reemplaza
    /// a "null". Duplicados en <paramref name="sources"/> (mismo
    /// SourceEntityType+SourceId repetido) se resuelven una sola vez.
    /// </summary>
    Task<IReadOnlyDictionary<(SourceEntityType SourceEntityType, Guid SourceId), MovementDetail>> GetManyBySourceAsync(
        IReadOnlyList<(SourceEntityType SourceEntityType, Guid SourceId)> sources,
        CancellationToken cancellationToken = default);
}
