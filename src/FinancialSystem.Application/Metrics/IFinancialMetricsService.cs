namespace FinancialSystem.Application.Metrics;

// ── Modelos de resultado ──────────────────────────────────────────────────────

public sealed record PeriodSummary(
    DateOnly From,
    DateOnly To,
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal NetBalance,
    decimal SavingsRate,
    int ClassifiedCount,
    int ConfirmedCount,
    int ReviewedCount,
    string Currency);

public sealed record CategoryExpense(
    Guid CategoryId,
    string CategoryName,
    string CategoryDisplayName,
    decimal TotalAmount,
    int TransactionCount,
    decimal PercentageOfTotal);

public sealed record MonthlyTrendPoint(
    int Year,
    int Month,
    string MonthLabel,
    decimal TotalExpenses,
    decimal TotalIncome,
    decimal NetBalance,
    decimal SavingsRate);

public sealed record MonthComparison(
    PeriodSummary Current,
    PeriodSummary? Previous,
    decimal ExpenseVariation,
    double ExpenseVariationPct,
    IReadOnlyList<CategoryVariation> CategoryVariations);

public sealed record CategoryVariation(
    string CategoryDisplayName,
    decimal CurrentAmount,
    decimal PreviousAmount,
    decimal Variation,
    double VariationPct);

/// <summary>
/// Cobertura de clasificación de un período (Patch 0071/0072, PATCH-019, Épica L
/// "Visibilidad de cobertura" -- ver docs/RoadMaps/FinancialMcp-vNext.md). Responde
/// "¿qué porcentaje de los movimientos reales de este período ya está clasificado?" --
/// distinto de PeriodSummary, que solo describe lo YA clasificado sin decir nada sobre
/// cuánto queda pendiente (riesgo #4 de docs/PROJECT_STATUS.md).
///
/// DEFINICIÓN DE "CLASIFICADO" (consistente con el modelo vigente, sin alternativas):
///   Un movimiento (banco o tarjeta) está clasificado si y solo si existe un
///   ClassifiedMovement/ClassifiedMovementItem que lo referencia -- exactamente el
///   mismo criterio que ya usa IMovementsQueryService (MovementView.Status non-null =
///   clasificado, null = pendiente) para la pantalla Movimientos. No hay estados
///   intermedios: ClassifiedMovement exige MovementType/FinancialImpact/CategoryId
///   obligatorios por diseño (ver ClassifiedMovement.cs) -- si la fila existe, las
///   dimensiones obligatorias ya están completas.
///
/// TotalMovements = ClassifiedMovements + PendingMovements -- Patch 0072: ambos
/// números vienen de consultas COUNT directas contra la base (ver
/// FinancialMetricsService.GetClassificationCoverageAsync), sin materializar ningún
/// movimiento en memoria.
/// </summary>
public sealed record ClassificationCoverage(
    DateOnly From,
    DateOnly To,
    int TotalMovements,
    int ClassifiedMovements,
    int PendingMovements,
    decimal CoveragePercentage);

// ── Interfaz del servicio ─────────────────────────────────────────────────────

/// <summary>
/// Queries financieras de lectura pura sobre ClassifiedMovement.
/// Consumida por: Dashboard, MCP Server, Worker de insights.
/// Nunca persiste nada.
///
/// REGLA FUNDAMENTAL:
///   Solo ClassifiedMovement con FinancialImpact = Expense cuentan para gastos.
///   Income se suma a ingresos.
///   InternalMovement y DebtPayment se excluyen de todas las métricas.
/// </summary>
public interface IFinancialMetricsService
{
    Task<PeriodSummary> GetPeriodSummaryAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<IReadOnlyList<CategoryExpense>> GetExpensesByCategoryAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<IReadOnlyList<MonthlyTrendPoint>> GetMonthlyTrendAsync(
        int months, CancellationToken ct = default);

    Task<MonthComparison> CompareWithPreviousMonthAsync(
        int year, int month, CancellationToken ct = default);

    /// <summary>Ver ClassificationCoverage para la definición completa.</summary>
    Task<ClassificationCoverage> GetClassificationCoverageAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default);
}