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
}