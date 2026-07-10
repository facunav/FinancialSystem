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
/// Suggestion (K4) y Warning (K6): contexto de solo lectura tomado de IReviewEngine —
/// ninguno de los dos existe nunca en movimientos clasificados (el motor solo considera
/// pendientes, por diseño de MovementLoader). Ninguno implica una acción propia:
/// confirmar un match N↔M real, o resolver un grupo sospechoso, sigue siendo exclusivo
/// de group-reconciliation.html.
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
    MovementSuggestion? Suggestion,
    MovementWarning? Warning);

/// <summary>
/// Grupo sospechoso (K6) al que pertenece este movimiento, según ISuspicionDetector —
/// posible duplicado o posible split. GroupSize es el tamaño total del grupo (incluyendo
/// este movimiento), no la lista completa de FinancialMovement del grupo: alcanza para
/// que el usuario entienda que no está solo, sin reconstruir la pantalla de reconciliación
/// acá. Sin acción asociada — ver group-reconciliation.html para resolver el grupo.
/// </summary>
public sealed record MovementWarning(
    SuspicionReason Reason,
    string Description,
    int GroupSize);

/// <summary>
/// Mejor candidato que IReviewEngine encontró para este movimiento — ya sea la
/// coincidencia asignada (Matched, arriba del umbral de confianza) o, si no llegó al
/// umbral, el mejor near-miss reportado en Unmatched. Un solo candidato, no la lista
/// completa de RuleContribution — eso ya vive en group-reconciliation.html.
/// CandidateSource (K5): necesario para que quien confirme la sugerencia (vía
/// ConfirmMatchCommand) sepa a qué SourceEntityType mapear el candidato — siempre
/// LegacyDynamic o LegacyFixed, nunca banco/tarjeta (el motor solo emparoja
/// Reference con Candidate, nunca Reference con Reference).
/// </summary>
public sealed record MovementSuggestion(
    Guid CandidateSourceId,
    string CandidateDescription,
    decimal CandidateAmount,
    DateTime CandidateDate,
    MovementSource CandidateSource,
    MatchConfidence Confidence);

// ── Interfaz del servicio ─────────────────────────────────────────────────────

/// <summary>
/// Lectura combinada de movimientos de banco/tarjeta (Transaction/BankStatement)
/// para la pantalla Movimientos (Épica K): pendientes + sugerencias + sospechosos
/// (vía IReviewEngine, una sola ejecución por request) y ya clasificados (vía
/// ClassifiedMovement/ClassifiedMovementItem). Nunca persiste nada. No expone
/// confirmación de match N↔M ni resolución de grupos sospechosos — eso sigue
/// siendo exclusivo de group-reconciliation.html.
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
