using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Accounts;
using FinancialSystem.Application.Imports;
using FinancialSystem.Application.Insights;
using FinancialSystem.Application.Metrics;
using FinancialSystem.Application.Movements;
using FinancialSystem.Application.Parsing.Bbva;
using FinancialSystem.Application.Parsing.Bbva.Mastercard;
using FinancialSystem.Application.Parsing.Bbva.Visa;
using FinancialSystem.Application.Parsing.Mastercard;
using FinancialSystem.Application.Planning;
using FinancialSystem.Application.Review;
using FinancialSystem.Application.Suggestions;
using FinancialSystem.Infrastructure.Accounts;
using FinancialSystem.Infrastructure.Audit;
using FinancialSystem.Infrastructure.Imports;
using FinancialSystem.Infrastructure.Imports.BankStatements;
using FinancialSystem.Infrastructure.Imports.Consistency;
using FinancialSystem.Infrastructure.Imports.Diagnostics;
using FinancialSystem.Infrastructure.Insights;
using FinancialSystem.Infrastructure.Metrics;
using FinancialSystem.Infrastructure.Movements;
using FinancialSystem.Infrastructure.Persistence;
using FinancialSystem.Infrastructure.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        services.AddScoped<AppDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<AppDbContext>());

        services.Configure<FileIngestionOptions>(
            configuration.GetSection(FileIngestionOptions.SectionName));

        services.Configure<ReviewEngineOptions>(
            configuration.GetSection(ReviewEngineOptions.SectionName));

        services.AddSingleton<ITransactionNormalizer,
            Imports.Normalization.TransactionNormalizer>();

        // ── Parsers genéricos ─────────────────────────────────────────────────
        services.AddSingleton<IFileParser, Imports.Parsers.CsvFileParser>();
        services.AddSingleton<IFileParser, Imports.Parsers.ExcelWorkbookParser>();

        // ── Infraestructura PDF ───────────────────────────────────────────────
        services.AddSingleton<PdfPigTextExtractor>();
        services.AddSingleton<IPdfTextExtractor>(sp =>
            sp.GetRequiredService<PdfPigTextExtractor>());

        // ── Parsers PDF ───────────────────────────────────────────────────────
        services.AddSingleton<BbvaTransactionLineParser>();
        services.AddSingleton<MastercardTransactionLineParser>();
        services.AddSingleton<IFileParser, BbvaVisaStatementParser>();
        services.AddSingleton<IFileParser, BbvaMastercardStatementParser>();

        // ── Factory y router de archivos ──────────────────────────────────────
        services.AddSingleton<IFileParserFactory, Imports.Parsers.FileParserFactory>();
        services.AddSingleton<XlsBankStatementReader>();
        services.AddSingleton<BbvaBankStatementParser>();
        services.AddSingleton<BbvaBankStatementImporter>();
        services.AddSingleton<IFileImportHandler, BbvaBankStatementImportHandler>();
        services.AddSingleton<BbvaDebitCardParser>();
        services.AddSingleton<IFileImportHandler, BbvaDebitCardEnrichmentHandler>();
        services.AddSingleton<IFileImportHandler, TransactionImportHandler>();

        services.AddScoped<IFinancialMetricsService, FinancialMetricsService>();
        services.AddScoped<IMovementLoader, Review.MovementLoader>();
        services.AddScoped<IMovementsQueryService, MovementsQueryService>();
        services.AddScoped<IMovementLookupService, MovementLookupService>();
        services.AddSingleton<ISuspicionDetector, Review.SuspicionDetector>();
        services.AddScoped<IReviewEngine, Review.ReviewEngine>();

        // PR-S3: primera implementación real (ver docs/Architecture/PRS1analisismotorsugerencias.md
        // para el diseño y PR-S3 para la heurística — exact match de descripción normalizada
        // contra el historial de ClassifiedMovements, sin IA). Scoped (no Singleton, a
        // diferencia de ISuspicionDetector) porque depende de IApplicationDbContext, que
        // es Scoped — Singleton acá sería una captive dependency. Sin consumidores todavía.
        services.AddScoped<IClassificationSuggestionService, Suggestions.ClassificationSuggestionService>();

        services.AddSingleton<IImportFileValidator, ImportFileValidator>();

        // ── Verificación de integridad posterior a la importación (Patch 0056) ─────
        // Agregar una verificación nueva es agregar una implementación de
        // IImportConsistencyCheck acá -- FileImportRouter e ImportConsistencyVerifier no
        // cambian.
        services.AddSingleton<IImportConsistencyCheck, QuantityConsistencyCheck>();
        services.AddSingleton<IImportConsistencyCheck, MovementProvenanceCheck>();
        services.AddSingleton<IImportConsistencyCheck, ImportHistoryConsistencyCheck>();
        services.AddSingleton<IImportConsistencyVerifier, ImportConsistencyVerifier>();

        // ── Observabilidad y diagnóstico del pipeline (Patch 0057) ──────────────────
        services.AddSingleton<IImportPipelineDiagnostics, ImportPipelineDiagnostics>();

        services.AddSingleton<IFileImportRouter, FileImportRouter>();
        services.AddSingleton<IImportFileSink, ImportFileProcessingSink>();
        services.AddScoped<IImportHistoryQueryService, ImportHistoryQueryService>();
        services.AddScoped<IFinancialAccountQueryService, FinancialAccountQueryService>();
        services.AddScoped<IPlanningQueryService, PlanningQueryService>();
        services.AddScoped<IPlanningMatchSuggestionService, PlanningMatchSuggestionService>();

        // AuditReportService: orquestación de auditoría compartida entre
        // FinancialSystem.McpServer (AuditTools/AuditDatabaseTools) y FinancialMcp.Api
        // (Centro de Auditoría) -- ver AuditReportService.cs para el porqué de la
        // ubicación. Clase plana, sin interfaz propia (no reemplaza ni envuelve
        // ninguno de los servicios de arriba, solo los orquesta).
        services.AddScoped<AuditReportService>();


        // ── Insights (Ollama + OpenAI) ────────────────────────────────────────
        services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.SectionName));
        services.AddHttpClient<IFinancialInsightsService, OllamaFinancialInsightsService>()
            .ConfigureHttpClient((sp, client) =>
            {
                var ollama = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
                client.BaseAddress = new Uri(ollama.BaseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, ollama.TimeoutSeconds));
            });

        // ILocalAiService: misma sección "Ollama" y mismo criterio de configuración de
        // HttpClient que IFinancialInsightsService de arriba -- ningún appsettings nuevo.
        services.AddHttpClient<ILocalAiService, OllamaLocalAiService>()
            .ConfigureHttpClient((sp, client) =>
            {
                var ollama = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
                client.BaseAddress = new Uri(ollama.BaseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, ollama.TimeoutSeconds));
            });

        services.Configure<OpenAIOptions>(configuration.GetSection(OpenAIOptions.SectionName));
        services.PostConfigure<OpenAIOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.ApiKey))
                options.ApiKey = configuration["OPENAI_API_KEY"]
                    ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                    ?? string.Empty;
        });
        services.AddHttpClient<IOpenAIFinancialInsightsService, OpenAIFinancialInsightsService>()
            .ConfigureHttpClient((sp, client) =>
            {
                var openAi = sp.GetRequiredService<IOptions<OpenAIOptions>>().Value;
                var baseUrl = string.IsNullOrWhiteSpace(openAi.BaseUrl)
                    ? "https://api.openai.com/"
                    : openAi.BaseUrl;
                client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, openAi.TimeoutSeconds));
            });

        return services;
    }
}