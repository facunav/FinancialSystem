using FinancialSystem.Application.Suggestions;
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
/// Suggestions (PR-S4): recomendaciones de solo lectura tomadas de
/// IClassificationSuggestionService — igual que Warning, nunca presente en
/// movimientos ya clasificados (no tiene sentido sugerir algo que el usuario ya
/// clasificó). Lista vacía, nunca null, cuando el motor no encontró señal. A
/// diferencia de Warning, sí existe una acción razonable asociada (aplicar el valor
/// sugerido al clasificar), pero ese wiring de UI todavía no existe — ver PR-S5.
/// PR-L4: hasta acá existía un campo Suggestion con esta misma responsabilidad,
/// pero producido por el viejo motor de matching contra movimientos legacy — se
/// retiró junto con todo ese backend. Este campo es una reintroducción con una
/// fuente y una forma completamente distintas (ver ClassificationSuggestion), no
/// una restauración de aquel.
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
    MovementWarning? Warning,
    IReadOnlyList<ClassificationSuggestion> Suggestions);

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
/// IReviewEngine, una sola ejecución por request) + sugerencias de clasificación
/// (vía IClassificationSuggestionService, PR-S4, una sola ejecución por request,
/// independiente de IReviewEngine) y ya clasificados (vía
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
