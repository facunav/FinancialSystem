namespace FinancialSystem.Application.Insights;

public interface IFinancialInsightsService
{
    Task<TransactionInsightResult> GetInsightsAsync(
        IReadOnlyList<TransactionSummary> transactions,
        CancellationToken cancellationToken = default);

    Task<TransactionInsightResult> GetInsightsAsync(
        TransactionInsightRequest request,
        CancellationToken cancellationToken = default);
}
