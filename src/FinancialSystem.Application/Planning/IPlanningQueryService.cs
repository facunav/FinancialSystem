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

// ── Interfaz del servicio ─────────────────────────────────────────────────────

/// <summary>
/// Queries de solo lectura sobre Planificación Mensual (PlanningMonth/PlanningItem).
/// Nunca persiste nada — las escrituras viven en
/// FinancialSystem.Application.Planning.Commands. Sin consumidores todavía: ningún
/// endpoint expone este módulo en este patch (ver docs/Epics/Epica-PlanificacionMensual.md).
/// </summary>
public interface IPlanningQueryService
{
    /// <summary>PlanningMonth de un período puntual, con sus PlanningItem. Null si no existe.</summary>
    Task<PlanningMonthDetail?> GetByPeriodAsync(DateTime period, CancellationToken ct = default);

    /// <summary>El PlanningMonth más reciente por Period, con sus PlanningItem. Null si no existe ninguno.</summary>
    Task<PlanningMonthDetail?> GetLatestAsync(CancellationToken ct = default);

    /// <summary>Resumen calculado del mes (sección 6.5). Null si el PlanningMonth no existe.</summary>
    Task<PlanningMonthSummary?> GetSummaryAsync(Guid planningMonthId, CancellationToken ct = default);
}
