using System.Net;
using System.Net.Http.Json;
using FinancialSystem.Api.Authentication;
using FinancialSystem.Api.Endpoints;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Accounts;
using FinancialSystem.Application.Metrics;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FinancialMcp.Api.Tests.Authentication;

/// <summary>
/// Cubre la protección aplicada en el Patch 0060 (PATCH-011) a los endpoints de
/// administración de catálogos: /api/accounts, /api/categories y /api/counterparties
/// quedan detrás de RequireAuthorization() (mismo esquema "ApiKey" de los Patches
/// 0058/0059).
///
/// DECISIÓN EXPLÍCITA (sección 2 del patch): se protegen también las lecturas (GET),
/// no solo la escritura. Mismo criterio ya aplicado a Importaciones/Movimientos en el
/// Patch 0059 -- esta es una aplicación single-user (ver PROJECT_STATUS.md, riesgo
/// #1: "cualquiera con acceso de red puede LEER... todos los datos financieros"), sin
/// ningún consumidor legítimo que deba poder leer datos maestros sin autenticarse. Los
/// tests de este archivo verifican explícitamente que los GET también devuelven 401
/// sin clave -- eso es lo que distingue esta decisión de la alternativa
/// (solo proteger escritura), y es lo que este patch pide probar.
///
/// Se mapean las extensiones REALES de FinancialMcp.Api (MapFinancialAccountEndpoints,
/// MapCategoryEndpoints, MapCounterpartyEndpoints -- código de producción, sin
/// duplicarlo) sobre un host mínimo propio, mismo criterio que
/// ApiKeyAuthenticationTests/ProtectedEndpointsTests (Patches 0058/0059): Program.cs
/// requiere una conexión real a PostgreSQL en su arranque, fuera del alcance de este
/// patch.
/// </summary>
public class MasterDataProtectedEndpointsTests
{
    private const string ValidApiKey = "clave-secreta-de-prueba";

    public static IEnumerable<object[]> ProtectedRoutes =>
    [
        // Financial Accounts
        [HttpMethod.Get, "/api/accounts"],
        [HttpMethod.Get, $"/api/accounts/{Guid.NewGuid()}"],
        [HttpMethod.Post, "/api/accounts"],
        [HttpMethod.Put, $"/api/accounts/{Guid.NewGuid()}"],
        [HttpMethod.Delete, $"/api/accounts/{Guid.NewGuid()}"],
        // Categories
        [HttpMethod.Get, "/api/categories"],
        [HttpMethod.Post, "/api/categories"],
        [HttpMethod.Put, $"/api/categories/{Guid.NewGuid()}"],
        [HttpMethod.Delete, $"/api/categories/{Guid.NewGuid()}"],
        // Counterparties
        [HttpMethod.Get, "/api/counterparties"],
        [HttpMethod.Post, "/api/counterparties"],
        [HttpMethod.Put, $"/api/counterparties/{Guid.NewGuid()}"],
        [HttpMethod.Delete, $"/api/counterparties/{Guid.NewGuid()}"],
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
    public async Task FinancialAccounts_Create_WithValidApiKey_Succeeds()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.PostAsJsonAsync(
            "/api/accounts",
            new CreateFinancialAccountRequest("Cuenta de prueba", "Bank", null, null, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task FinancialAccounts_Update_WithValidApiKey_ReachesTheHandler()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.PutAsJsonAsync(
            $"/api/accounts/{Guid.NewGuid()}",
            new UpdateFinancialAccountRequest(null, null, null, null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FinancialAccounts_Deactivate_WithValidApiKey_ReachesTheHandler()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.DeleteAsync($"/api/accounts/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FinancialAccounts_GetAll_WithValidApiKey_Succeeds()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.GetAsync("/api/accounts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Categories_Create_WithValidApiKey_Succeeds()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.PostAsJsonAsync(
            "/api/categories",
            new CreateCategoryRequest("Categoría de prueba", null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Categories_Update_WithValidApiKey_ReachesTheHandler()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.PutAsJsonAsync(
            $"/api/categories/{Guid.NewGuid()}",
            new UpdateCategoryRequest(null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Categories_Deactivate_WithValidApiKey_ReachesTheHandler()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.DeleteAsync($"/api/categories/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Categories_GetAll_WithValidApiKey_Succeeds()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.GetAsync("/api/categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Counterparties_Create_WithValidApiKey_Succeeds()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.PostAsJsonAsync(
            "/api/counterparties",
            new CreateCounterpartyRequest("Contraparte de prueba", null, null, null, null, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Counterparties_Update_WithValidApiKey_ReachesTheHandler()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.PutAsJsonAsync(
            $"/api/counterparties/{Guid.NewGuid()}",
            new UpdateCounterpartyRequest(null, null, null, null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Counterparties_Deactivate_WithValidApiKey_ReachesTheHandler()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.DeleteAsync($"/api/counterparties/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Counterparties_GetAll_WithValidApiKey_Succeeds()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.GetAsync("/api/counterparties");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnrelatedEndpoint_MetricsSummary_RemainsPublic_NoRegressionFromThisPatch()
    {
        // /api/metrics no forma parte de este patch (Financial Accounts/Categories/
        // Counterparties) -- debe seguir funcionando exactamente igual, sin ninguna clave.
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

                    services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                    services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

                    services.AddSingleton<IFinancialAccountQueryService, FakeFinancialAccountQueryService>();
                    services.AddSingleton<IFinancialMetricsService, FakeFinancialMetricsService>();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapFinancialAccountEndpoints();
                        endpoints.MapCategoryEndpoints();
                        endpoints.MapCounterpartyEndpoints();
                        endpoints.MapMetricsEndpoints();
                    });
                });
            });

        return await hostBuilder.StartAsync();
    }

    private sealed class FakeFinancialAccountQueryService : IFinancialAccountQueryService
    {
        public Task<IReadOnlyList<FinancialAccountSummary>> GetAllAsync(
            bool includeDeactivated = false, string? search = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FinancialAccountSummary>>([]);

        public Task<FinancialAccountSummary?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<FinancialAccountSummary?>(null);
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
    }
}
