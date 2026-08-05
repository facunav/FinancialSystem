using FinancialSystem.Api.Authentication;
using FinancialSystem.Api.Endpoints;
using FinancialSystem.Api.ErrorHandling;
using FinancialSystem.Api.Imports;
using FinancialSystem.Api.Validation;
using FinancialSystem.Application;
using FinancialSystem.Infrastructure;
using FinancialSystem.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Patch 0058 (PATCH-009): infraestructura de autenticación, extendida en el Patch
// 0067A con Cookie Authentication para la UI web (ver
// ApiAuthenticationServiceCollectionExtensions.AddApiAuthentication) -- ApiKey y
// Cookie coexisten, cada .RequireAuthorization() ya existente acepta cualquiera de
// los dos sin que ningún grupo de endpoints necesite tocarse.
builder.Services.AddApiAuthentication(builder.Configuration);

// Patch 0063 (PATCH-014): límite explícito y configurable para POST /api/imports (ver
// ImportUploadOptions) -- se aplica más abajo, después de app.Build(), acotado a esa
// ruta puntual (no afecta al resto de la API).
builder.Services.Configure<ImportUploadOptions>(
    builder.Configuration.GetSection(ImportUploadOptions.SectionName));

// Patch 0064 (PATCH-015): registro de ProblemDetails para excepciones no controladas
// (ver ApiProblemDetailsServiceCollectionExtensions) -- app.UseExceptionHandler() más
// abajo es lo que efectivamente activa el manejo global.
builder.Services.AddApiProblemDetails(builder.Environment);

// Patch 0065 (PATCH-016): módulo piloto de validación estructurada (FluentValidation),
// acotado a Categories -- ver src/FinancialMcp.Api/Validation/README.md.
// AddValidatorsFromAssemblyContaining registra automáticamente todo IValidator<T>
// declarado en este assembly (hoy: CreateCategoryRequestValidator y
// UpdateCategoryRequestValidator); no hace falta registrar cada uno a mano.
builder.Services.AddValidatorsFromAssemblyContaining<CreateCategoryRequestValidator>();

var app = builder.Build();

// Patch 0064 (PATCH-015): manejo global de excepciones -- primera línea del pipeline,
// para envolver TODO lo que viene después (autenticación, archivos estáticos,
// endpoints). UseExceptionHandler() sin delegate propio, combinado con
// AddProblemDetails() de arriba, ya devuelve por default de ASP.NET Core un
// ProblemDetails (RFC 9457) con status 500 para cualquier excepción no controlada --
// sin exponer stack traces ni nombres de clases salvo en Development (ver
// ApiProblemDetailsServiceCollectionExtensions). No reemplaza ni duplica ninguna
// respuesta de error ya controlada por los endpoints (Results.BadRequest/NotFound/
// Problem siguen exactamente igual -- este middleware nunca llega a intervenir en
// esos casos, porque no son excepciones).
app.UseExceptionHandler();

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
// correcto (después de routing/archivos estáticos, antes de mapear endpoints).
// Patch 0067A: ahora resuelve entre ApiKey y Cookie por request (ver
// ApiAuthenticationServiceCollectionExtensions.AddApiAuthentication) -- el orden del
// middleware no cambió, solo lo que AddApiAuthentication registra por detrás.
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

// Patch 0067A: login/logout/verificación de sesión para la UI web -- ver AuthEndpoints.
app.MapAuthEndpoints();

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