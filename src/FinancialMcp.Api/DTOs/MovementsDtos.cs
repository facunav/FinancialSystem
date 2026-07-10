using FinancialSystem.Application.Movements;

namespace FinancialSystem.Api.DTOs;

// ── GET /api/movements ──────────────────────────────────────────────────────
// DTO propio de este endpoint. Contiene solo lo que la pantalla Movimientos
// necesita: listar, clasificar y asignar/reasignar cuenta — pendientes y ya
// clasificados (K3), con sospechosos (K6) como contexto adicional de solo lectura.
//
// PR-L4: hasta acá también incluía Suggestion (K4/K5) — se retiró junto con todo
// el backend de matching contra movimientos legacy. Ver MovementView (Application)
// para el detalle de por qué.

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
        m.Warning is null ? null : MovementWarningDto.Create(m.Warning));
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
