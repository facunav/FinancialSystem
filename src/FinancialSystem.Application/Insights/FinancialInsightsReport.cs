namespace FinancialSystem.Application.Insights;

public sealed record FinancialInsightsReport(
    string Summary,
    IReadOnlyList<string> GastosHormiga,
    IReadOnlyList<string> CategoriasDominantes,
    IReadOnlyList<string> Suscripciones,
    IReadOnlyList<string> HabitosRepetitivos);
