using FinancialSystem.Api.Endpoints;
using FinancialSystem.Application;
using FinancialSystem.Infrastructure;
using FinancialSystem.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

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

app.MapGet("/", () => Results.Redirect("/dashboard.html"));

app.MapCategoryEndpoints();
app.MapCounterpartyEndpoints();
app.MapMetricsEndpoints();

app.Run();