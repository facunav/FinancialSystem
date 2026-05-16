namespace FinancialSystem.Application.Insights;

public sealed record TransactionInsightResult(
    bool Success,
    string Summary,
    string? TopCategoriesHint = null,
    string? AnomaliesHint = null,
    string? RawResponse = null,
    string? Error = null);
