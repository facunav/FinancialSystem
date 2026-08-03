using System.Net;
using System.Net.Http.Json;
using FinancialSystem.Api.Authentication;
using FinancialSystem.Api.DTOs;
using FinancialSystem.Api.Endpoints;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Imports;
using FinancialSystem.Application.Movements;
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
/// Cubre la protección aplicada en el Patch 0059 (PATCH-010) a los endpoints de
/// Importaciones y Movimientos: /api/imports, /api/movements, /api/movement-review,
/// /api/transactions y /api/bank-statements quedan detrás de
/// RequireAuthorization() (mismo esquema "ApiKey" del Patch 0058). Ningún otro
/// endpoint cambia -- /api/categories se incluye acá justamente para probar que un
/// grupo NO tocado por este patch sigue siendo público (ausencia de regresiones).
///
/// Se mapean las extensiones REALES de FinancialMcp.Api (MapImportBatchEndpoints,
/// MapMovementsEndpoints, etc. -- código de producción, sin duplicarlo) sobre un host
/// mínimo propio con dependencias fake/InMemory, por el mismo motivo que
/// ApiKeyAuthenticationTests (Patch 0058): Program.cs requiere una conexión real a
/// PostgreSQL en su arranque, fuera del alcance de este patch.
/// </summary>
public class ProtectedEndpointsTests
{
    private const string ValidApiKey = "clave-secreta-de-prueba";

    public static IEnumerable<object[]> ProtectedRoutes =>
    [
        [HttpMethod.Get, "/api/imports/history"],
        [HttpMethod.Get, "/api/movements"],
        [HttpMethod.Get, $"/api/movement-review/effective-date-suggestion?counterpartyId={Guid.NewGuid()}&originalDate=2026-01-01"],
        [HttpMethod.Put, $"/api/transactions/{Guid.NewGuid()}/financial-account"],
        [HttpMethod.Put, $"/api/bank-statements/{Guid.NewGuid()}/financial-account"],
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
    public async Task ImportsHistory_WithValidApiKey_ReachesTheHandler()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.GetAsync("/api/imports/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Movements_WithValidApiKey_ReachesTheHandler()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.GetAsync("/api/movements");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MovementReview_EffectiveDateSuggestion_WithValidApiKey_ReachesTheHandler()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.GetAsync(
            $"/api/movement-review/effective-date-suggestion?counterpartyId={Guid.NewGuid()}&originalDate=2026-01-01");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Transactions_AssignFinancialAccount_WithValidApiKey_ReachesTheHandler()
    {
        // 404 (no existe esa Transaction) en vez de 401 -- prueba que la autorización
        // dejó pasar la request hasta el handler real, sin verificar el resto de la
        // lógica de negocio (sin cambios en este patch).
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.PutAsJsonAsync(
            $"/api/transactions/{Guid.NewGuid()}/financial-account",
            new AssignFinancialAccountRequest(null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BankStatements_AssignFinancialAccount_WithValidApiKey_ReachesTheHandler()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.PutAsJsonAsync(
            $"/api/bank-statements/{Guid.NewGuid()}/financial-account",
            new AssignFinancialAccountRequest(null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnrelatedEndpoint_Categories_RemainsPublic_NoRegressionFromThisPatch()
    {
        // /api/categories no forma parte de este patch (Importaciones y Movimientos) --
        // debe seguir funcionando exactamente igual, sin ninguna clave.
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/api/categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        if (method == HttpMethod.Put)
            request.Content = JsonContent.Create(new AssignFinancialAccountRequest(null));
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

                    services.AddSingleton<IImportHistoryQueryService, FakeImportHistoryQueryService>();
                    services.AddSingleton<IMovementsQueryService, FakeMovementsQueryService>();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapImportBatchEndpoints();
                        endpoints.MapMovementsEndpoints();
                        endpoints.MapMovementReviewEndpoints();
                        endpoints.MapTransactionEndpoints();
                        endpoints.MapBankStatementEndpoints();
                        endpoints.MapCategoryEndpoints();
                    });
                });
            });

        return await hostBuilder.StartAsync();
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
}
