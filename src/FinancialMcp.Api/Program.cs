using FinancialSystem.Api.Authentication;
using FinancialSystem.Api.Endpoints;
using FinancialSystem.Application;
using FinancialSystem.Infrastructure;
using FinancialSystem.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Patch 0058 (PATCH-009): infraestructura de autenticación. Ningún endpoint la exige
// todavía -- se agrega acá, ya integrada al pipeline, para que los próximos patches solo
// necesiten agregar .RequireAuthorization() a los grupos de endpoints correspondientes.
builder.Services.AddApiKeyAuthentication(builder.Configuration);

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

// Patch 0058 (PATCH-009): middleware de autenticación/autorización, ya en el orden
// correcto (después de routing/archivos estáticos, antes de mapear endpoints). Sin
// ningún .RequireAuthorization() todavía, no cambia el comportamiento observable de
// ningún endpoint -- ver ApiKeyAuthenticationHandler para el porqué.
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/dashboard.html"));

app.MapCategoryEndpoints();
app.MapCounterpartyEndpoints();
app.MapFinancialAccountEndpoints();
app.MapTransactionEndpoints();
app.MapBankStatementEndpoints();
app.MapMetricsEndpoints();
app.MapMovementReviewEndpoints();
app.MapMovementsEndpoints();
app.MapImportBatchEndpoints();
app.MapInvestigationEndpoints();
app.MapAuditEndpoints();
app.MapPlanningEndpoints();

app.Run();