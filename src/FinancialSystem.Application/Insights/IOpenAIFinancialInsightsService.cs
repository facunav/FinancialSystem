namespace FinancialSystem.Application.Insights;

public interface IOpenAIFinancialInsightsService
{
    Task<OpenAIFinancialInsightResult> GetStructuredInsightsAsync(
        IReadOnlyList<TransactionSummary> transactions,
        CancellationToken cancellationToken = default);

    Task<OpenAIFinancialInsightResult> GetStructuredInsightsAsync(
        TransactionInsightRequest request,
        CancellationToken cancellationToken = default);
}
