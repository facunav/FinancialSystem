namespace FinancialSystem.Application.Insights;

public sealed record TransactionInsightRequest(
    IReadOnlyList<TransactionSummary> Transactions,
    string? Focus = null);
