using FinancialSystem.Domain.Review;

namespace FinancialSystem.Api.DTOs;

// ── GET /api/movements ──────────────────────────────────────────────────────
// DTO propio de este endpoint — deliberadamente no reutiliza FinancialMovementDto
// (ese pertenece a /api/movement-review/*, pensado para la pantalla de Migración
// desde Excel). Contiene solo lo que la pantalla Movimientos necesita: listar,
// clasificar y asignar/reasignar cuenta.

public sealed record MovementListItemDto(
    Guid SourceId,
    DateTime Date,
    string Description,
    decimal Amount,
    string Currency,
    string Source,
    Guid? FinancialAccountId)
{
    public static MovementListItemDto Create(FinancialMovement m) => new(
        m.SourceId,
        m.Date,
        m.Description,
        m.Amount,
        m.Currency,
        m.Source.ToString(),
        m.FinancialAccountId);
}
