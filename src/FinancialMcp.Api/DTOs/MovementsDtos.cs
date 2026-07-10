using FinancialSystem.Application.Movements;

namespace FinancialSystem.Api.DTOs;

// ── GET /api/movements ──────────────────────────────────────────────────────
// DTO propio de este endpoint — deliberadamente no reutiliza FinancialMovementDto
// (ese pertenece a /api/movement-review/*, pensado para la pantalla de Migración
// desde Excel). Contiene solo lo que la pantalla Movimientos necesita: listar,
// clasificar y asignar/reasignar cuenta — pendientes y ya clasificados (K3).

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
    string? FinancialImpact)
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
        m.FinancialImpact?.ToString());
}
