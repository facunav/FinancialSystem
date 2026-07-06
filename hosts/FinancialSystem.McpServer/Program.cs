using FinancialSystem.Application;
using FinancialSystem.Infrastructure;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(consoleLogOptions =>
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var host = builder.Build();

await DatabaseMigrationExtensions.ApplyMigrationsAsync(host.Services, "FinancialSystem.McpServer");

await host.RunAsync();