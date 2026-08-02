namespace FinancialSystem.Application.Planning;

// ── Modelos de resultado ──────────────────────────────────────────────────────
// Neutros: no son PlanningMonth/PlanningItem (entidades EF), ni DTOs de HTTP.
// Existen para que el modelo interno pueda evolucionar sin romper el contrato
// público de la API — el mapeo entidad → estos modelos vive en
// PlanningQueryService (Infrastructure); el mapeo modelo → DTO de HTTP queda para
// cuando exista el endpoint (fuera de alcance de este patch).

public sealed record PlanningItemSummary(
    Guid Id,
    string Title,
    decimal? ExpectedAmount,
    DateTime? DueDate,
    bool IsPaid,
    DateTime? PaidAt);

public sealed record PlanningMonthDetail(
    Guid Id,
    DateTime Period,
    decimal? ExpectedIncome,
    IReadOnlyList<PlanningItemSummary> Items);

/// <summary>
/// Resumen del mes — ver docs/Epics/Epica-PlanificacionMensual.md, sección 6.5.
/// Available es null cuando ExpectedIncome es null: nunca se calcula un valor en
/// su lugar ("Disponible ... solo si ExpectedIncome existe").
/// </summary>
public sealed record PlanningMonthSummary(
    Guid PlanningMonthId,
    DateTime Period,
    decimal? ExpectedIncome,
    decimal TotalPlanned,
    decimal Paid,
    decimal Pending,
    decimal? Available);

/// <summary>
/// Resumen mínimo para el Dashboard (Patch 0047) — únicamente conteos, nunca montos
/// ni comparaciones. TotalItems = PendingCount + PaidCount siempre. NextDueDate es el
/// DueDate más próximo entre los PlanningItem pendientes que tengan uno cargado; null
/// si ninguno tiene DueDate o si no hay pendientes.
/// </summary>
public sealed record PlanningDashboardSummary(
    Guid PlanningMonthId,
    int TotalItems,
    int PendingCount,
    int PaidCount,
    DateTime? NextDueDate);

// ── Interfaz del servicio ─────────────────────────────────────────────────────

/// <summary>
/// Queries de solo lectura sobre Planificación Mensual (PlanningMonth/PlanningItem).
/// Nunca persiste nada — las escrituras viven en
/// FinancialSystem.Application.Planning.Commands.
/// </summary>
public interface IPlanningQueryService
{
    /// <summary>PlanningMonth de un período puntual, con sus PlanningItem. Null si no existe.</summary>
    Task<PlanningMonthDetail?> GetByPeriodAsync(DateTime period, CancellationToken ct = default);

    /// <summary>El PlanningMonth más reciente por Period, con sus PlanningItem. Null si no existe ninguno.</summary>
    Task<PlanningMonthDetail?> GetLatestAsync(CancellationToken ct = default);

    /// <summary>Resumen calculado del mes (sección 6.5). Null si el PlanningMonth no existe.</summary>
    Task<PlanningMonthSummary?> GetSummaryAsync(Guid planningMonthId, CancellationToken ct = default);

    /// <summary>Resumen de conteos para el Dashboard (sección 8 de la épica). Null si no existe PlanningMonth para ese período.</summary>
    Task<PlanningDashboardSummary?> GetDashboardSummaryAsync(DateTime period, CancellationToken ct = default);
}
