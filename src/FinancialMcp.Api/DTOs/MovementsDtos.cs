using FinancialSystem.Application.Movements;

namespace FinancialSystem.Api.DTOs;

// ── GET /api/movements ──────────────────────────────────────────────────────
// DTO propio de este endpoint — deliberadamente no reutiliza FinancialMovementDto
// (ese pertenece a /api/movement-review/*, pensado para la pantalla de Migración
// desde Excel). Contiene solo lo que la pantalla Movimientos necesita: listar,
// clasificar y asignar/reasignar cuenta — pendientes y ya clasificados (K3), con
// sugerencias (K4) y sospechosos (K6) como contexto adicional de solo lectura.

public sealed record MovementListItemDto(
    Guid SourceId,
    DateTime Date,
    string Description,
    decimal Amount,
    string Currency,
    string Source,
    Guid? FinancialAccountId,
    // "Pending" cuando el movimiento todavía no tiene ClassifiedMovementItem.
    // Si no, el Status real del ClassifiedMovement ("Reviewed"/"Confirmed").
    string Status,
    Guid? CategoryId,
    Guid? CounterpartyId,
    string? MovementType,
    string? FinancialImpact,
    // K4: contexto de solo lectura, null si el motor no encontró ningún candidato.
    // Nunca presente en movimientos ya clasificados (ver MovementView.Suggestion).
    MovementSuggestionDto? Suggestion,
    // K6: contexto de solo lectura, null si el movimiento no cayó en ningún grupo
    // sospechoso. Nunca presente en movimientos ya clasificados (ver MovementView.Warning).
    MovementWarningDto? Warning)
{
    public static MovementListItemDto Create(MovementView m) => new(
        m.SourceId,
        m.Date,
        m.Description,
        m.Amount,
        m.Currency,
        m.Source.ToString(),
        m.FinancialAccountId,
        m.Status?.ToString() ?? "Pending",
        m.CategoryId,
        m.CounterpartyId,
        m.MovementType?.ToString(),
        m.FinancialImpact?.ToString(),
        m.Suggestion is null ? null : MovementSuggestionDto.Create(m.Suggestion),
        m.Warning is null ? null : MovementWarningDto.Create(m.Warning));
}

public sealed record MovementSuggestionDto(
    Guid CandidateSourceId,
    string CandidateDescription,
    decimal CandidateAmount,
    DateTime CandidateDate,
    string CandidateSource,
    string Confidence)
{
    public static MovementSuggestionDto Create(MovementSuggestion s) => new(
        s.CandidateSourceId,
        s.CandidateDescription,
        s.CandidateAmount,
        s.CandidateDate,
        s.CandidateSource.ToString(),
        s.Confidence.ToString());
}

public sealed record MovementWarningDto(
    string Reason,
    string Description,
    int GroupSize)
{
    public static MovementWarningDto Create(MovementWarning w) => new(
        w.Reason.ToString(),
        w.Description,
        w.GroupSize);
}
