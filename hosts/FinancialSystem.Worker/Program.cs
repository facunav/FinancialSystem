using FinancialSystem.Application;
using FinancialSystem.Application.Imports;
using FinancialSystem.Application.Insights;
using FinancialSystem.Infrastructure;
using FinancialSystem.Infrastructure.Persistence;
using FinancialSystem.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<FileIngestionOptions>(
    builder.Configuration.GetSection(FileIngestionOptions.SectionName));
builder.Services.Configure<InsightsWorkerOptions>(
    builder.Configuration.GetSection(InsightsWorkerOptions.SectionName));
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<ImportsFolderWatcherHostedService>();
builder.Services.AddHostedService<TransactionInsightsWorker>();

var host = builder.Build();
await DatabaseMigrationExtensions.ApplyMigrationsAsync(host.Services, "FinancialSystem.Worker");

await host.RunAsync();
