using System.ComponentModel;
using System.Text;
using FinancialSystem.Application.Metrics;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace FinancialSystem.McpServer.Tools;

/// <summary>
/// Herramientas financieras expuestas al LLM via MCP.
///
/// PRINCIPIO: este archivo no contiene lógica de negocio.
/// Solo delega a IFinancialMetricsService y formatea la respuesta
/// para que el modelo pueda razonar sobre ella en lenguaje natural.
///
/// Las respuestas son texto plano estructurado (no JSON) porque
/// los modelos razonan mejor con texto que con JSON crudo.
/// </summary>
[McpServerToolType]
public sealed class FinancialTools
{
    private readonly IFinancialMetricsService _metrics;

    public FinancialTools(IFinancialMetricsService metrics)
    {
        _metrics = metrics;
    }

    // ?? Resumen mensual ???????????????????????????????????????????????????????

    [McpServerTool]
    [Description(
        "Devuelve el resumen financiero de un mes: ingresos, gastos, ahorro y cantidad de movimientos procesados. " +
        "Usar cuando el usuario pregunta cuánto gastó, cuánto ahorró o cómo le fue en un mes específico.")]
    public async Task<string> GetMonthlySummary(
        [Description("Año. Ejemplo: 2026")] int year,
        [Description("Mes (1-12). Ejemplo: 6 para junio")] int month,
        CancellationToken ct = default)
    {
        if (month is < 1 or > 12)
            return "Error: el mes debe estar entre 1 y 12.";

        try
        {
            var from = new DateOnly(year, month, 1);
            var to = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
            var summary = await _metrics.GetPeriodSummaryAsync(from, to, ct);
            var newBalance = summary.NetBalance >= 0 ? "positivo" : "negativo";

            var sb = new StringBuilder();
            sb.AppendLine($"Resumen financiero: {summary.From:MMMM yyyy}");
            sb.AppendLine($"  Ingresos:    {FormatArs(summary.TotalIncome)}");
            sb.AppendLine($"  Gastos:      {FormatArs(summary.TotalExpenses)}");
            sb.AppendLine($"  Balance:     {FormatArs(summary.NetBalance)} ({newBalance})");
            sb.AppendLine($"  Tasa ahorro: {summary.SavingsRate}%");
            sb.AppendLine($"  Movimientos: {summary.ClassifiedCount} procesados ({summary.ConfirmedCount} confirmados, {summary.ReviewedCount} revisados)");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error al obtener el resumen: {ex.Message}";
        }
    }

    // ?? Gastos por categoría ??????????????????????????????????????????????????

    [McpServerTool]
    [Description(
        "Devuelve los gastos reales agrupados por categoría para un período. " +
        "Usar cuando el usuario pregunta en qué gasta más, cómo distribuye sus gastos, " +
        "o cuánto gastó en una categoría específica (alimentación, salud, transporte, etc).")]
    public async Task<string> GetExpensesByCategory(
        [Description("Fecha de inicio en formato yyyy-MM-dd. Ejemplo: 2026-06-01")] string from,
        [Description("Fecha de fin en formato yyyy-MM-dd. Ejemplo: 2026-06-30")] string to,
        CancellationToken ct = default)
    {
        if (!DateOnly.TryParse(from, out var fromDate) || !DateOnly.TryParse(to, out var toDate))
            return "Error: formato de fecha inválido. Usar yyyy-MM-dd.";
        if (fromDate >= toDate)
            return "Error: la fecha de inicio debe ser anterior a la de fin.";

        try
        {
            var categories = await _metrics.GetExpensesByCategoryAsync(fromDate, toDate, ct);
            if (categories.Count == 0)
                return $"No hay gastos registrados entre {fromDate:dd/MM/yyyy} y {toDate:dd/MM/yyyy}.";

            var sb = new StringBuilder();
            sb.AppendLine($"Gastos por categoría ({fromDate:dd/MM/yyyy} - {toDate:dd/MM/yyyy}):");
            var total = categories.Sum(c => c.TotalAmount);
            sb.AppendLine($"Total: {FormatArs(total)}");
            sb.AppendLine();
            foreach (var cat in categories)
            {
                sb.AppendLine($"  {cat.CategoryDisplayName,-20} {FormatArs(cat.TotalAmount),14}  ({cat.PercentageOfTotal}%)  {cat.TransactionCount} movs.");
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error al obtener gastos por categoría: {ex.Message}";
        }
    }

    // ?? Tendencia mensual ?????????????????????????????????????????????????????

    [McpServerTool]
    [Description(
        "Devuelve la evolución de gastos e ingresos mes a mes durante los últimos N meses. " +
        "Usar cuando el usuario pregunta si sus gastos están subiendo, bajando, " +
        "cómo evolucionaron sus finanzas o quiere ver una tendencia histórica.")]
    public async Task<string> GetMonthlyTrend(
        [Description("Cantidad de meses hacia atrás (1-24). Ejemplo: 6 para ver los últimos 6 meses.")] int months,
        CancellationToken ct = default)
    {
        if (months is < 1 or > 24)
            return "Error: months debe estar entre 1 y 24.";

        try
        {
            var trend = await _metrics.GetMonthlyTrendAsync(months, ct);
            if (trend.Count == 0)
                return "No hay datos suficientes para mostrar la tendencia.";

            var sb = new StringBuilder();
            sb.AppendLine($"Evolución de los últimos {months} meses:");
            sb.AppendLine($"{"Mes",-12} {"Gastos",14} {"Ingresos",14} {"Balance",14} {"Ahorro",8}");
            sb.AppendLine(new string('-', 64));
            foreach (var p in trend)
            {
                var balSign = p.NetBalance >= 0 ? "+" : "";
                sb.AppendLine($"{p.MonthLabel,-12} {FormatArs(p.TotalExpenses),14} {FormatArs(p.TotalIncome),14} {balSign}{FormatArs(p.NetBalance),13} {p.SavingsRate,6}%");
            }

            // Tendencia simple: comparar último mes con el primero
            if (trend.Count >= 2)
            {
                var first = trend[0].TotalExpenses;
                var last = trend[^1].TotalExpenses;
                var diff = last - first;
                var pct = first > 0 ? Math.Round((double)(diff / first) * 100, 1) : 0.0;
                sb.AppendLine();
                sb.AppendLine(diff > 0
                    ? $"Tendencia: los gastos subieron {FormatArs(diff)} ({pct}%) en el período."
                    : $"Tendencia: los gastos bajaron {FormatArs(Math.Abs(diff))} ({Math.Abs(pct)}%) en el período.");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error al obtener la tendencia: {ex.Message}";
        }
    }

    // ?? Comparación con mes anterior ??????????????????????????????????????????

    [McpServerTool]
    [Description(
        "Compara los gastos de un mes contra el mes anterior, incluyendo variación por categoría. " +
        "Usar cuando el usuario pregunta si gastó más o menos que el mes pasado, " +
        "qué categorías aumentaron, o si está mejorando su situación financiera.")]
    public async Task<string> CompareWithPreviousMonth(
        [Description("Año. Ejemplo: 2026")] int year,
        [Description("Mes (1-12). Ejemplo: 6 para junio")] int month,
        CancellationToken ct = default)
    {
        if (month is < 1 or > 12)
            return "Error: el mes debe estar entre 1 y 12.";

        try
        {
            var comparison = await _metrics.CompareWithPreviousMonthAsync(year, month, ct);
            var curr = comparison.Current;
            var prev = comparison.Previous;

            var sb = new StringBuilder();
            sb.AppendLine($"Comparación: {curr.From:MMMM yyyy} vs {prev?.From.ToString("MMMM yyyy") ?? "mes anterior (sin datos)"}");
            sb.AppendLine();
            sb.AppendLine($"  Gastos actuales:   {FormatArs(curr.TotalExpenses)}");

            if (prev is not null)
            {
                sb.AppendLine($"  Gastos anteriores: {FormatArs(prev.TotalExpenses)}");
                var sign = comparison.ExpenseVariation >= 0 ? "+" : "";
                sb.AppendLine($"  Variación:         {sign}{FormatArs(comparison.ExpenseVariation)} ({sign}{comparison.ExpenseVariationPct}%)");
                sb.AppendLine();

                var trending = comparison.ExpenseVariationPct > 2 ? "subieron" :
                               comparison.ExpenseVariationPct < -2 ? "bajaron" : "se mantuvieron estables";
                sb.AppendLine($"Los gastos {trending} respecto al mes anterior.");
                sb.AppendLine();

                // Top 3 categorías con mayor variación
                var topVariations = comparison.CategoryVariations
                    .Where(v => v.Variation != 0)
                    .Take(5)
                    .ToList();

                if (topVariations.Count > 0)
                {
                    sb.AppendLine("Categorías con mayor variación:");
                    foreach (var v in topVariations)
                    {
                        var s = v.Variation >= 0 ? "+" : "";
                        sb.AppendLine($"  {v.CategoryDisplayName,-20} {s}{FormatArs(v.Variation),10} ({s}{v.VariationPct}%)");
                    }
                }
            }
            else
            {
                sb.AppendLine("  No hay datos del mes anterior para comparar.");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error al comparar meses: {ex.Message}";
        }
    }

    // ?? Helpers ???????????????????????????????????????????????????????????????

    private static string FormatArs(decimal amount) =>
        amount.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("es-AR"));
}