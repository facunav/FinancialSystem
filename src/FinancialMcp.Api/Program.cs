using FinancialMcp.Api.Endpoints;
using FinancialSystem.Application;
using FinancialSystem.Application.Reconciliation;
using FinancialSystem.Infrastructure;
using FinancialSystem.Infrastructure.Persistence;
using FinancialSystem.Infrastructure.Reconciliation;
using FinancialSystem.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddReconciliation(builder.Configuration);
builder.Services.AddScoped<MovementHydrationService>();

var app = builder.Build();

await DatabaseMigrationExtensions.ApplyMigrationsAsync(app.Services, "FinancialMcp.Api");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseHttpsRedirection();

// Dashboard como pantalla principal
app.MapGet("/", () => Results.Redirect("/dashboard.html"));

app.MapReconciliationEndpoints();
app.MapCategoryEndpoints();
app.MapMetricsEndpoints();

app.Run();