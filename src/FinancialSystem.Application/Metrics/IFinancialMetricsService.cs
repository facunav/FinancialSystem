namespace FinancialSystem.Application.Metrics;

// ── Modelos de resultado (pure records, sin dependencias externas) ─────────────

/// <summary>
/// Resumen financiero de un período (mes, trimestre, año).
/// Responde: ¿qué pasó con mi dinero en este período?
/// </summary>
public sealed record PeriodSummary(
    DateOnly From,
    DateOnly To,
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal NetBalance,
    decimal SavingsRate,      // (Income - RealExpense) / Income * 100
    int ProcessedCount,       // Total de ProcessedExpense en el período
    int ConfirmedCount,
    int ReviewedCount,
    string Currency);

/// <summary>
/// Gasto por categoría en un período.
/// Responde: ¿en qué gasto?
/// </summary>
public sealed record CategoryExpense(
    Guid CategoryId,
    string CategoryName,
    string CategoryDisplayName,
    decimal TotalAmount,
    int TransactionCount,
    decimal PercentageOfTotal);   // % del total de RealExpense

/// <summary>
/// Evolución mensual de gastos e ingresos.
/// Responde: ¿cómo evolucionan mis gastos?
/// </summary>
public sealed record MonthlyTrendPoint(
    int Year,
    int Month,
    string MonthLabel,           // "Ene 2026"
    decimal TotalExpenses,       // solo RealExpense
    decimal TotalIncome,
    decimal NetBalance,
    decimal SavingsRate);

/// <summary>
/// Comparación de un mes contra el anterior.
/// Responde: ¿estoy gastando más o menos?
/// </summary>
public sealed record MonthComparison(
    PeriodSummary Current,
    PeriodSummary? Previous,
    decimal ExpenseVariation,     // absoluta: actual - anterior
    double ExpenseVariationPct,  // porcentual
    IReadOnlyList<CategoryVariation> CategoryVariations);

public sealed record CategoryVariation(
    string CategoryDisplayName,
    decimal CurrentAmount,
    decimal PreviousAmount,
    decimal Variation,
    double VariationPct);

// ── Interfaz del servicio ─────────────────────────────────────────────────────

/// <summary>
/// Queries financieras de lectura pura sobre ProcessedExpense.
/// Esta interfaz es el contrato que usan el Dashboard, el MCP y el Worker de insights.
/// Nunca persiste nada. Solo agrega y proyecta datos ya existentes.
///
/// REGLA FUNDAMENTAL:
///   Solo los ProcessedExpense con FinancialImpact = RealExpense cuentan
///   para calcular gastos. Income va a ingresos. InternalTransfer y DebtPayment
///   se excluyen de todas las métricas de gasto.
/// </summary>
public interface IFinancialMetricsService
{
    /// <summary>
    /// Resumen financiero de un período.
    /// GET /api/metrics/summary?year=2026&month=6
    /// MCP: GetMonthlySummary(month, year)
    /// </summary>
    Task<PeriodSummary> GetPeriodSummaryAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// Gastos agrupados por categoría, ordenados de mayor a menor.
    /// GET /api/metrics/by-category?from=...&to=...
    /// MCP: GetExpensesByCategory(from, to)
    /// </summary>
    Task<IReadOnlyList<CategoryExpense>> GetExpensesByCategoryAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// Evolución mensual de los últimos N meses.
    /// GET /api/metrics/monthly-trend?months=6
    /// MCP: GetMonthlyTrend(months)
    /// </summary>
    Task<IReadOnlyList<MonthlyTrendPoint>> GetMonthlyTrendAsync(
        int months, CancellationToken ct = default);

    /// <summary>
    /// Compara el mes indicado contra el anterior.
    /// Incluye variación por categoría para detectar cambios.
    /// MCP: CompareWithPreviousMonth(month, year)
    /// </summary>
    Task<MonthComparison> CompareWithPreviousMonthAsync(
        int year, int month, CancellationToken ct = default);
}