using System.Net;
using System.Net.Http.Json;
using FinancialSystem.Api.Authentication;
using FinancialSystem.Api.DTOs;
using FinancialSystem.Api.Endpoints;
using FinancialSystem.Application;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Imports;
using FinancialSystem.Application.Metrics;
using FinancialSystem.Application.Movements;
using FinancialSystem.Application.Planning;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FinancialMcp.Api.Tests.Authentication;

/// <summary>
/// Cubre la protección aplicada en el Patch 0061 (PATCH-012) a los endpoints
/// restantes de Planning, Audit e Investigations: /api/planning-months,
/// /api/planning-items, /api/audit y /api/investigations quedan detrás de
/// RequireAuthorization() (mismo esquema "ApiKey" de los Patches 0058/0059/0060).
///
/// DECISIÓN EXPLÍCITA (sección 2 del patch): se protegen también las lecturas (GET),
/// incluyendo /api/audit/status y /api/audit/report -- mismo criterio ya aplicado a
/// Importaciones/Movimientos (Patch 0059) y datos maestros (Patch 0060): aplicación
/// single-user, sin ningún consumidor legítimo que deba leer sin autenticarse. Los
/// tests de este archivo verifican explícitamente que los GET también devuelven 401
/// sin clave.
///
/// OPERACIONES SENSIBLES DE SOLO LECTURA (sección 3 del patch): /api/audit/report
/// ejecuta BuildFullAuditReportAsync -- recorre movimientos, clasificaciones y
/// sugerencias del período para generar el reporte completo -- y queda protegido
/// igual que el resto del grupo /api/audit; no requiere (ni recibe) ningún manejo
/// especial porque RequireAuthorization() en el grupo ya lo cubre.
///
/// Se mapean las extensiones REALES de FinancialMcp.Api (MapPlanningEndpoints,
/// MapAuditEndpoints, MapInvestigationEndpoints -- código de producción, sin
/// duplicarlo) sobre un host mínimo propio, mismo criterio que
/// ProtectedEndpointsTests/MasterDataProtectedEndpointsTests (Patches 0059/0060):
/// Program.cs requiere una conexión real a PostgreSQL en su arranque, fuera del
/// alcance de este patch. Los handlers de Planning/Audit/Investigations dependen
/// únicamente de IApplicationDbContext (+ IDateTimeProvider) -- se registran vía la
/// extensión real AddApplication(), sin duplicar su wiring.
/// </summary>
public class PlanningAuditInvestigationsProtectedEndpointsTests
{
    private const string ValidApiKey = "clave-secreta-de-prueba";

    public static IEnumerable<object[]> ProtectedRoutes =>
    [
        // Planning months
        [HttpMethod.Post, "/api/planning-months"],
        [HttpMethod.Get, "/api/planning-months?period=2026-09-01"],
        [HttpMethod.Get, "/api/planning-months/latest"],
        [HttpMethod.Post, $"/api/planning-months/{Guid.NewGuid()}/copy"],
        [HttpMethod.Get, $"/api/planning-months/{Guid.NewGuid()}/summary"],
        [HttpMethod.Put, $"/api/planning-months/{Guid.NewGuid()}/expected-income"],
        [HttpMethod.Get, $"/api/planning-months/{Guid.NewGuid()}/match-suggestions"],
        [HttpMethod.Get, "/api/planning-months/dashboard-summary?period=2026-09-01"],
        // Planning items
        [HttpMethod.Post, "/api/planning-items"],
        [HttpMethod.Put, $"/api/planning-items/{Guid.NewGuid()}"],
        [HttpMethod.Delete, $"/api/planning-items/{Guid.NewGuid()}"],
        [HttpMethod.Post, $"/api/planning-items/{Guid.NewGuid()}/pay"],
        [HttpMethod.Post, $"/api/planning-items/{Guid.NewGuid()}/unpay"],
        // Audit
        [HttpMethod.Get, "/api/audit/status"],
        [HttpMethod.Get, "/api/audit/report"],
        [HttpMethod.Post, "/api/audit/reviews"],
        // Investigations
        [HttpMethod.Post, "/api/investigations"],
    ];

    [Theory]
    [MemberData(nameof(ProtectedRoutes))]
    public async Task ProtectedRoute_WithoutApiKey_Returns401(HttpMethod method, string path)
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();

        var response = await client.SendAsync(BuildRequest(method, path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ProtectedRoutes))]
    public async Task ProtectedRoute_WithInvalidApiKey_Returns401(HttpMethod method, string path)
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, "clave-incorrecta");

        var response = await client.SendAsync(BuildRequest(method, path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PlanningMonths_Create_WithValidApiKey_Succeeds()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.PostAsJsonAsync(
            "/api/planning-months",
            new CreatePlanningMonthRequest(new DateTime(2026, 9, 1)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task PlanningMonths_GetByPeriod_WithValidApiKey_ReachesTheHandler()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.GetAsync("/api/planning-months?period=2026-09-01");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PlanningMonths_UpdateExpectedIncome_WithValidApiKey_ReachesTheHandler()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.PutAsJsonAsync(
            $"/api/planning-months/{Guid.NewGuid()}/expected-income",
            new UpdateExpectedIncomeRequest(1000m));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PlanningItems_Delete_WithValidApiKey_ReachesTheHandler()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.DeleteAsync($"/api/planning-items/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PlanningItems_MarkAsPaid_WithValidApiKey_ReachesTheHandler()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.PostAsync($"/api/planning-items/{Guid.NewGuid()}/pay", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Audit_Status_WithValidApiKey_Succeeds()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.GetAsync("/api/audit/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Audit_Reviews_WithValidApiKey_Succeeds()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.PostAsJsonAsync(
            "/api/audit/reviews",
            new ReviewMovementsRequest([new ReviewMovementRequestItem(SourceEntityType.Transaction, Guid.NewGuid())]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Investigations_Create_WithValidApiKey_Succeeds()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.PostAsJsonAsync(
            "/api/investigations",
            new CreateInvestigationRequest("¿Por qué este movimiento no tiene categoría?", null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task UnrelatedEndpoint_MetricsSummary_RemainsPublic_NoRegressionFromThisPatch()
    {
        // /api/metrics no forma parte de este patch (Planning/Audit/Investigations) --
        // debe seguir funcionando exactamente igual, sin ninguna clave.
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/api/metrics/summary?year=2026&month=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        if (method == HttpMethod.Post || method == HttpMethod.Put)
            request.Content = JsonContent.Create(new { });
        return request;
    }

    private static async Task<IHost> CreateHostAsync(string? configuredApiKey)
    {
        var dbName = Guid.NewGuid().ToString();

        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureAppConfiguration(config =>
                {
                    if (configuredApiKey is not null)
                    {
                        config.AddInMemoryCollection(
                        [
                            new KeyValuePair<string, string?>("ApiAuthentication:ApiKey", configuredApiKey)
                        ]);
                    }
                });
                webHost.ConfigureServices((context, services) =>
                {
                    services.AddRouting();
                    services.AddApiKeyAuthentication(context.Configuration);
                    services.AddApplication();

                    services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                    services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

                    services.AddSingleton<IPlanningQueryService, FakePlanningQueryService>();
                    services.AddSingleton<IPlanningMatchSuggestionService, FakePlanningMatchSuggestionService>();
                    services.AddSingleton<IImportHistoryQueryService, FakeImportHistoryQueryService>();
                    services.AddSingleton<IMovementsQueryService, FakeMovementsQueryService>();
                    services.AddSingleton<IFinancialMetricsService, FakeFinancialMetricsService>();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapPlanningEndpoints();
                        endpoints.MapAuditEndpoints();
                        endpoints.MapInvestigationEndpoints();
                        endpoints.MapMetricsEndpoints();
                    });
                });
            });

        return await hostBuilder.StartAsync();
    }

    private sealed class FakePlanningQueryService : IPlanningQueryService
    {
        public Task<PlanningMonthDetail?> GetByPeriodAsync(DateTime period, CancellationToken ct = default) =>
            Task.FromResult<PlanningMonthDetail?>(null);

        public Task<PlanningMonthDetail?> GetLatestAsync(CancellationToken ct = default) =>
            Task.FromResult<PlanningMonthDetail?>(null);

        public Task<PlanningMonthSummary?> GetSummaryAsync(Guid planningMonthId, CancellationToken ct = default) =>
            Task.FromResult<PlanningMonthSummary?>(null);

        public Task<PlanningDashboardSummary?> GetDashboardSummaryAsync(DateTime period, CancellationToken ct = default) =>
            Task.FromResult<PlanningDashboardSummary?>(null);
    }

    private sealed class FakePlanningMatchSuggestionService : IPlanningMatchSuggestionService
    {
        public Task<IReadOnlyList<PlanningItemMatchSuggestion>?> GetSuggestionsAsync(
            Guid planningMonthId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PlanningItemMatchSuggestion>?>(null);
    }

    private sealed class FakeImportHistoryQueryService : IImportHistoryQueryService
    {
        public Task<IReadOnlyList<ImportBatchSummary>> GetHistoryAsync(int take = 50, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ImportBatchSummary>>([]);

        public Task<ImportBatchDetail?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<ImportBatchDetail?>(null);
    }

    private sealed class FakeMovementsQueryService : IMovementsQueryService
    {
        public Task<IReadOnlyList<MovementView>> GetAsync(
            DateOnly from, DateOnly to, Guid? financialAccountId, string? search, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MovementView>>([]);
    }

    private sealed class FakeFinancialMetricsService : IFinancialMetricsService
    {
        public Task<PeriodSummary> GetPeriodSummaryAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
            Task.FromResult(new PeriodSummary(from, to, 0m, 0m, 0m, 0m, 0, 0, 0, "ARS"));

        public Task<IReadOnlyList<CategoryExpense>> GetExpensesByCategoryAsync(
            DateOnly from, DateOnly to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CategoryExpense>>([]);

        public Task<IReadOnlyList<MonthlyTrendPoint>> GetMonthlyTrendAsync(int months, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MonthlyTrendPoint>>([]);

        public Task<MonthComparison> CompareWithPreviousMonthAsync(int year, int month, CancellationToken ct = default) =>
            Task.FromResult(new MonthComparison(
                new PeriodSummary(new DateOnly(year, month, 1), new DateOnly(year, month, 1), 0m, 0m, 0m, 0m, 0, 0, 0, "ARS"),
                null, 0m, 0, []));

        public Task<ClassificationCoverage> GetClassificationCoverageAsync(
            DateOnly from, DateOnly to, CancellationToken ct = default) =>
            Task.FromResult(new ClassificationCoverage(from, to, 0, 0, 0, 0m));
    }
}
