using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;

namespace FinancialSystem.Application.Movements;

// ── Modelo de resultado ────────────────────────────────────────────────────────
// Neutro: no es FinancialMovement (pensado para el motor de sugerencias) ni
// ClassifiedMovement/ClassifiedMovementItem (entidades EF). Existe porque la
// pantalla Movimientos necesita datos de dos fuentes distintas (movimientos
// pendientes y ya clasificados) combinados en una sola forma.

/// <summary>
/// Status null = pendiente (sin ClassifiedMovementItem todavía). No-null = clasificado,
/// con el Status real de su ClassifiedMovement (Reviewed/Confirmed).
/// </summary>
public sealed record MovementView(
    Guid SourceId,
    DateTime Date,
    string Description,
    decimal Amount,
    string Currency,
    MovementSource Source,
    Guid? FinancialAccountId,
    ClassificationStatus? Status,
    Guid? CategoryId,
    Guid? CounterpartyId,
    MovementType? MovementType,
    FinancialImpact? FinancialImpact);

// ── Interfaz del servicio ─────────────────────────────────────────────────────

/// <summary>
/// Lectura combinada de movimientos de banco/tarjeta (Transaction/BankStatement)
/// para la pantalla Movimientos (Épica K): pendientes (vía IMovementLoader) +
/// ya clasificados (vía ClassifiedMovement/ClassifiedMovementItem). Nunca persiste
/// nada. No usa IReviewEngine — sin sugerencias, sin matching, sin sospechosos.
/// </summary>
public interface IMovementsQueryService
{
    Task<IReadOnlyList<MovementView>> GetAsync(
        DateOnly from,
        DateOnly to,
        Guid? financialAccountId,
        string? search,
        CancellationToken cancellationToken = default);
}
