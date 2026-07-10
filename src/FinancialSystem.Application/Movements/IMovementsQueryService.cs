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
/// Suggestion (K4): contexto de solo lectura tomado de IReviewEngine — nunca existe en
/// movimientos clasificados (el motor solo considera pendientes, por diseño de
/// MovementLoader). No implica ninguna acción; confirmar un match sigue siendo
/// exclusivo de group-reconciliation.html.
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
    MovementSuggestion? Suggestion);

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
