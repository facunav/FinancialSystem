using FinancialSystem.Api.Authentication;
using FinancialSystem.Api.Endpoints;
using FinancialSystem.Api.Imports;
using FinancialSystem.Application;
using FinancialSystem.Infrastructure;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Patch 0058 (PATCH-009): infraestructura de autenticación. Ningún endpoint la exige
// todavía -- se agrega acá, ya integrada al pipeline, para que los próximos patches solo
// necesiten agregar .RequireAuthorization() a los grupos de endpoints correspondientes.
builder.Services.AddApiKeyAuthentication(builder.Configuration);

// Patch 0063 (PATCH-014): límite explícito y configurable para POST /api/imports (ver
// ImportUploadOptions) -- se aplica más abajo, después de app.Build(), acotado a esa
// ruta puntual (no afecta al resto de la API).
builder.Services.Configure<ImportUploadOptions>(
    builder.Configuration.GetSection(ImportUploadOptions.SectionName));

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

// Patch 0063 (PATCH-014): límite de tamaño explícito para POST /api/imports, sin
// depender del default implícito de Kestrel (~28.6 MB) ni del de FormOptions (128 MB)
// -- ninguno de los dos queda documentado en ningún archivo de configuración hoy.
// IHttpMaxRequestBodySizeFeature es el mecanismo estándar de ASP.NET Core para
// Minimal APIs (RequestSizeLimitAttribute es exclusivo del pipeline de filtros de
// MVC, no aplica acá) -- se fija por request, antes de que el endpoint lea el form,
// así que tiene que registrarse antes de MapImportBatchEndpoints en el pipeline.
// Acotado a POST /api/imports puntualmente: el resto de la API sigue con el límite
// por default del servidor, sin cambios de comportamiento fuera de este endpoint.
app.Use(async (context, next) =>
{
    if (HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Path.StartsWithSegments("/api/imports", StringComparison.OrdinalIgnoreCase))
    {
        var maxRequestBodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxRequestBodySizeFeature is not null && !maxRequestBodySizeFeature.IsReadOnly)
        {
            var uploadOptions = context.RequestServices.GetRequiredService<IOptions<ImportUploadOptions>>();
            maxRequestBodySizeFeature.MaxRequestBodySize = uploadOptions.Value.MaxFileSizeBytes;
        }
    }

    await next();
});

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