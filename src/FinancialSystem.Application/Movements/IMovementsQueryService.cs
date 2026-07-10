using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;

namespace FinancialSystem.Application.Movements;

// ── Modelo de resultado ────────────────────────────────────────────────────────
// Neutro: no es FinancialMovement (pensado para el motor de revisión) ni
// ClassifiedMovement/ClassifiedMovementItem (entidades EF). Existe porque la
// pantalla Movimientos necesita datos de dos fuentes distintas (movimientos
// pendientes y ya clasificados) combinados en una sola forma.

/// <summary>
/// Status null = pendiente (sin ClassifiedMovementItem todavía). No-null = clasificado,
/// con el Status real de su ClassifiedMovement (Reviewed/Confirmed).
/// Warning (K6): contexto de solo lectura tomado de IReviewEngine — nunca existe en
/// movimientos clasificados (el motor solo considera pendientes, por diseño de
/// MovementLoader). No implica ninguna acción propia; resolver un grupo sospechoso
/// no tiene ninguna pantalla hoy (group-reconciliation.html, que lo exponía, se
/// retiró en PR-L4 junto con el backend de matching Legacy que sostenía).
///
/// PR-L4: hasta acá también existía Suggestion (K4/K5) — la mejor coincidencia que
/// IReviewEngine encontraba contra un movimiento legacy candidato, con acción de
/// confirmar. Se retiró junto con todo el backend de matching, no queda productor
/// posible. Ver ReviewResult.cs para dónde debería integrarse un futuro motor de
/// recomendaciones — no necesariamente con esta misma forma.
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
    FinancialImpact? FinancialImpact,
    MovementWarning? Warning);

/// <summary>
/// Grupo sospechoso (K6) al que pertenece este movimiento, según ISuspicionDetector —
/// posible duplicado o posible split. GroupSize es el tamaño total del grupo (incluyendo
/// este movimiento), no la lista completa de FinancialMovement del grupo: alcanza para
/// que el usuario entienda que no está solo. Sin acción asociada.
/// </summary>
public sealed record MovementWarning(
    SuspicionReason Reason,
    string Description,
    int GroupSize);

// ── Interfaz del servicio ─────────────────────────────────────────────────────

/// <summary>
/// Lectura combinada de movimientos de banco/tarjeta (Transaction/BankStatement)
/// para la pantalla Movimientos (Épica K): pendientes + sospechosos (vía
/// IReviewEngine, una sola ejecución por request) y ya clasificados (vía
/// ClassifiedMovement/ClassifiedMovementItem). Nunca persiste nada.
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
