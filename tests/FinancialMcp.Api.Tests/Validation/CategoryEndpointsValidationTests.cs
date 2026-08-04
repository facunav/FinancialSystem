using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialSystem.Api.Authentication;
using FinancialSystem.Api.Endpoints;
using FinancialSystem.Api.Validation;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FinancialMcp.Api.Tests.Validation;

/// <summary>
/// Cubre el endpoint /api/categories de punta a punta con el validador de
/// FluentValidation ya integrado (Patch 0065, PATCH-016) -- CreateCategoryRequestValidator/
/// UpdateCategoryRequestValidatorTests cubren las reglas en aislamiento; este archivo
/// confirma que el endpoint efectivamente las usa, con el mismo formato de respuesta
/// que ya tenía (Results.BadRequest(string)) y sin regresiones sobre una solicitud
/// válida.
///
/// Se mapea la extensión REAL de FinancialMcp.Api (MapCategoryEndpoints -- código de
/// producción, sin duplicarlo) sobre un host mínimo propio con AppDbContext InMemory,
/// mismo criterio que MasterDataProtectedEndpointsTests (Patch 0060): Program.cs
/// requiere una conexión real a PostgreSQL en su arranque, fuera del alcance de este
/// patch.
/// </summary>
public class CategoryEndpointsValidationTests
{
    private const string ValidApiKey = "clave-secreta-de-prueba";

    [Fact]
    public async Task Create_WithValidRequest_Succeeds()
    {
        using var host = await CreateHostAsync();
        using var client = AuthorizedClient(host);

        var response = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Salud", null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithMissingDisplayName_ReturnsBadRequestWithoutTouchingTheDatabase()
    {
        using var host = await CreateHostAsync();
        using var client = AuthorizedClient(host);

        var response = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(string.Empty, null));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("displayName es requerido", body);

        var db = host.Services.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.Categories.CountAsync());
    }

    [Fact]
    public async Task Create_WithWhitespaceOnlyDisplayName_ReturnsBadRequest()
    {
        using var host = await CreateHostAsync();
        using var client = AuthorizedClient(host);

        var response = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("   ", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithDisplayNameExceedingMaxLength_ReturnsBadRequest()
    {
        using var host = await CreateHostAsync();
        using var client = AuthorizedClient(host);
        var tooLong = new string('a', CreateCategoryRequestValidator.DisplayNameMaxLength + 1);

        var response = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(tooLong, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithNameExceedingMaxLength_ReturnsBadRequest()
    {
        using var host = await CreateHostAsync();
        using var client = AuthorizedClient(host);
        var tooLong = new string('a', CreateCategoryRequestValidator.NameMaxLength + 1);

        var response = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Salud", tooLong));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithValidDisplayName_Succeeds()
    {
        using var host = await CreateHostAsync();
        using var client = AuthorizedClient(host);
        var id = await CreateCategoryAsync(client, "Salud");

        var response = await client.PutAsJsonAsync(
            $"/api/categories/{id}", new UpdateCategoryRequest("Salud actualizada", null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithNullDisplayName_StillSucceeds_NoRegressionOnExistingBehavior()
    {
        // Un DisplayName nulo en Update nunca fue un error (significa "no cambiar este
        // campo") -- section de compatibilidad del patch.
        using var host = await CreateHostAsync();
        using var client = AuthorizedClient(host);
        var id = await CreateCategoryAsync(client, "Salud");

        var response = await client.PutAsJsonAsync($"/api/categories/{id}", new UpdateCategoryRequest(null, 20));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithDisplayNameExceedingMaxLength_ReturnsBadRequest()
    {
        using var host = await CreateHostAsync();
        using var client = AuthorizedClient(host);
        var id = await CreateCategoryAsync(client, "Salud");
        var tooLong = new string('a', UpdateCategoryRequestValidator.DisplayNameMaxLength + 1);

        var response = await client.PutAsJsonAsync($"/api/categories/{id}", new UpdateCategoryRequest(tooLong, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<Guid> CreateCategoryAsync(HttpClient client, string displayName)
    {
        // Se lee el body crudo en vez de deserializar a un DTO propio -- evita
        // depender de la convención de casing (camelCase) que use la serialización
        // JSON del servidor, sin necesidad de reproducirla acá.
        var response = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(displayName, null));
        var raw = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private static HttpClient AuthorizedClient(IHost host)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);
        return client;
    }

    private static async Task<IHost> CreateHostAsync()
    {
        var dbName = Guid.NewGuid().ToString();

        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureAppConfiguration(config =>
                {
                    config.AddInMemoryCollection(
                    [
                        new KeyValuePair<string, string?>("ApiAuthentication:ApiKey", ValidApiKey)
                    ]);
                });
                webHost.ConfigureServices((context, services) =>
                {
                    services.AddRouting();
                    services.AddApiKeyAuthentication(context.Configuration);
                    services.AddValidatorsFromAssemblyContaining<CreateCategoryRequestValidator>();

                    services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                    services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapCategoryEndpoints());
                });
            });

        return await hostBuilder.StartAsync();
    }
}
