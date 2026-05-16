namespace FinancialSystem.Application.Insights;

public sealed record OpenAIFinancialInsightResult(
    bool Success,
    FinancialInsightsReport? Report = null,
    string? RawResponse = null,
    string? Error = null);
