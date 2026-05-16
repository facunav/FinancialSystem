namespace FinancialSystem.Application.Insights;

public sealed class InsightsWorkerOptions
{
    public const string SectionName = "InsightsWorker";

    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 5;

    public int TransactionBatchSize { get; set; } = 50;

    /// <summary>OpenAI, Ollama, or Both.</summary>
    public string Provider { get; set; } = InsightsProviders.Ollama;
}
