using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Imports;
using FinancialSystem.Application.Insights;
using FinancialSystem.Infrastructure.Imports;
using FinancialSystem.Infrastructure.Insights;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));
        services.AddScoped<AppDbContext>(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.Configure<FileIngestionOptions>(configuration.GetSection(FileIngestionOptions.SectionName));
        services.Configure<PdfStatementParseOptions>(configuration.GetSection(PdfStatementParseOptions.SectionName));

        services.AddSingleton<ITransactionNormalizer, Imports.Normalization.TransactionNormalizer>();
        services.AddSingleton<IFileParser, Imports.Parsers.CsvFileParser>();
        services.AddSingleton<IFileParser, Imports.Parsers.PdfStatementParser>();
        services.AddSingleton<IFileParser, Imports.Parsers.ExcelWorkbookParser>();
        services.AddSingleton<IFileParserFactory, Imports.Parsers.FileParserFactory>();
        services.AddSingleton<IImportFileSink, ImportFileProcessingSink>();

        services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.SectionName));
        services.AddHttpClient<IFinancialInsightsService, OllamaFinancialInsightsService>()
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
            {
                options.ApiKey = configuration["OPENAI_API_KEY"]
                    ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                    ?? string.Empty;
            }
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
