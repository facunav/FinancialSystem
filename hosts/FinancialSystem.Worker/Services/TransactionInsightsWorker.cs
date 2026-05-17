using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Insights;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Worker.Services;

public sealed class TransactionInsightsWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<InsightsWorkerOptions> workerOptions,
    ILogger<TransactionInsightsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = workerOptions.Value;
        if (!options.Enabled)
        {
            logger.LogInformation("Transaction insights worker is disabled");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, options.IntervalMinutes));
        logger.LogInformation(
            "Transaction insights worker started (provider {Provider}, every {IntervalMinutes} min, batch {BatchSize})",
            options.Provider,
            options.IntervalMinutes,
            options.TransactionBatchSize);

        await RunAnalysisCycleAsync(options, stoppingToken);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunAnalysisCycleAsync(options, stoppingToken);
        }
    }

    private async Task RunAnalysisCycleAsync(InsightsWorkerOptions options, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            var transactions = await db.Transactions
                .AsNoTracking()
                .OrderByDescending(t => t.Date)
                .Take(Math.Max(1, options.TransactionBatchSize))
                .ToListAsync(cancellationToken);

            if (transactions.Count == 0)
            {
                logger.LogDebug("No transactions in database for insights");
                return;
            }

            var summaries = transactions
                .Select(TransactionSummary.FromTransaction)
                .ToList();

            var provider = options.Provider.Trim();
            var runOpenAi = provider.Equals(InsightsProviders.OpenAI, StringComparison.OrdinalIgnoreCase)
                || provider.Equals(InsightsProviders.Both, StringComparison.OrdinalIgnoreCase);
            var runOllama = provider.Equals(InsightsProviders.Ollama, StringComparison.OrdinalIgnoreCase)
                || provider.Equals(InsightsProviders.Both, StringComparison.OrdinalIgnoreCase);

            if (!runOpenAi && !runOllama)
            {
                logger.LogWarning(
                    "Unknown InsightsWorker:Provider '{Provider}'. Use OpenAI, Ollama, or Both.",
                    options.Provider);
                return;
            }

            if (runOpenAi)
            {
                var openAi = scope.ServiceProvider.GetRequiredService<IOpenAIFinancialInsightsService>();
                await RunOpenAiInsightsAsync(openAi, summaries, cancellationToken);
            }

            if (runOllama)
            {
                var ollama = scope.ServiceProvider.GetRequiredService<IFinancialInsightsService>();
                await RunOllamaInsightsAsync(ollama, summaries, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Transaction insights cycle failed");
        }
    }

    private async Task RunOpenAiInsightsAsync(
        IOpenAIFinancialInsightsService openAi,
        IReadOnlyList<TransactionSummary> summaries,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Requesting structured financial insights for {Count} transactions from OpenAI",
            summaries.Count);

        var result = await openAi.GetStructuredInsightsAsync(summaries, cancellationToken);

        if (!result.Success || result.Report is null)
        {
            logger.LogWarning(
                "OpenAI financial insights failed: {Error}",
                result.Error ?? "unknown error");
            return;
        }

        var report = result.Report;
        logger.LogInformation("=== OpenAI Financial Insights ===");
        if (!string.IsNullOrWhiteSpace(report.Summary))
        {
            logger.LogInformation("Resumen: {Summary}", report.Summary);
        }

        LogSection("Gastos hormiga", report.GastosHormiga);
        LogSection("Categorías dominantes", report.CategoriasDominantes);
        LogSection("Suscripciones", report.Suscripciones);
        LogSection("Hábitos repetitivos", report.HabitosRepetitivos);
    }

    private async Task RunOllamaInsightsAsync(
        IFinancialInsightsService ollama,
        IReadOnlyList<TransactionSummary> summaries,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Requesting financial insights for {Count} transactions from Ollama",
            summaries.Count);

        var result = await ollama.GetInsightsAsync(summaries, cancellationToken);

        if (result.Success)
        {
            logger.LogInformation("=== Ollama Financial Insights ===\n{Summary}", result.Summary);
        }
        else
        {
            logger.LogWarning(
                "Ollama financial insights failed: {Error}",
                result.Error ?? "unknown error");
        }
    }

    private void LogSection(string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            logger.LogInformation("{Title}: (ninguno detectado)", title);
            return;
        }

        logger.LogInformation("{Title}:", title);
        foreach (var item in items)
        {
            logger.LogInformation("  - {Item}", item);
        }
    }
}
