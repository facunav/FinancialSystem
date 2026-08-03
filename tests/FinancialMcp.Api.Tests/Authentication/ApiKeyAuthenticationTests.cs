using System.Net;
using System.Net.Http.Json;
using FinancialSystem.Api.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinancialMcp.Api.Tests.Authentication;

/// <summary>
/// Cubre la infraestructura de autenticación agregada en el Patch 0058 (PATCH-009):
/// registro de AddAuthentication/AddAuthorization vía
/// ApiAuthenticationServiceCollectionExtensions.AddApiKeyAuthentication, el middleware
/// correspondiente, y el comportamiento del esquema "ApiKey" en sí.
///
/// IMPORTANTE: este patch NO protege ningún endpoint real de FinancialMcp.Api -- eso es
/// responsabilidad de los Patches 0059-0061. Estos tests arman un host mínimo propio
/// (Microsoft.AspNetCore.TestHost) con la MISMA infraestructura que Program.cs registra
/// (AddApiKeyAuthentication + UseAuthentication/UseAuthorization), y un endpoint de
/// prueba que en algunos tests exige autorización explícitamente -- simulando lo que un
/// patch futuro haría -- para poder probar el mecanismo de punta a punta sin depender de
/// una base de datos PostgreSQL real (que Program.cs sí requiere en su arranque, fuera
/// del alcance de este patch).
/// </summary>
public class ApiKeyAuthenticationTests
{
    private const string ValidApiKey = "clave-secreta-de-prueba";

    [Fact]
    public async Task PublicEndpoint_WithoutAnyApiKey_StillSucceeds()
    {
        // Sección 4 del patch (compatibilidad): mientras ningún endpoint exija
        // autorización, la API sigue funcionando igual que antes.
        using var host = await CreateHostAsync(configuredApiKey: ValidApiKey, requireAuthorizationOnEndpoint: false);
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PublicEndpoint_WithAnInvalidApiKey_StillSucceeds()
    {
        // Ausencia de regresiones: enviar una clave incorrecta no debe romper un endpoint
        // que no la exige -- el handler puede fallar la autenticación internamente, pero
        // eso no bloquea la request si nada pide autorización.
        using var host = await CreateHostAsync(configuredApiKey: ValidApiKey, requireAuthorizationOnEndpoint: false);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, "clave-incorrecta");

        var response = await client.GetAsync("/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithValidApiKey_AuthenticatesSuccessfully()
    {
        // "Registro correcto de autenticación": con una clave válida, un endpoint que SÍ
        // exige autorización (simulando un patch futuro) responde 200 y ve al usuario
        // autenticado.
        using var host = await CreateHostAsync(configuredApiKey: ValidApiKey, requireAuthorizationOnEndpoint: true);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.GetAsync("/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PingResponse>();
        Assert.True(body?.Authenticated);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutApiKey_IsRejected()
    {
        using var host = await CreateHostAsync(configuredApiKey: ValidApiKey, requireAuthorizationOnEndpoint: true);
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/ping");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithAnInvalidApiKey_IsRejected()
    {
        using var host = await CreateHostAsync(configuredApiKey: ValidApiKey, requireAuthorizationOnEndpoint: true);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, "clave-incorrecta");

        var response = await client.GetAsync("/ping");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MissingConfiguredApiKey_NeverAuthenticatesRegardlessOfWhatIsProvided()
    {
        // "Configuración inválida" (sección 3 del patch): si ApiAuthentication:ApiKey no
        // se configuró (queda vacío), ninguna clave provista debe autenticar -- una
        // configuración ausente no puede degradar en "aceptar cualquier cosa".
        using var host = await CreateHostAsync(configuredApiKey: string.Empty, requireAuthorizationOnEndpoint: true);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, "cualquier-valor");

        var response = await client.GetAsync("/ping");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PipelineInitializes_AndRegistersTheApiKeyScheme()
    {
        // "Inicialización correcta del pipeline" + "registro correcto de autenticación":
        // el host arranca sin errores y el esquema "ApiKey" queda resuelto por
        // IAuthenticationSchemeProvider, sin necesidad de hacer ninguna request HTTP.
        using var host = await CreateHostAsync(configuredApiKey: ValidApiKey, requireAuthorizationOnEndpoint: false);

        var schemeProvider = host.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = await schemeProvider.GetSchemeAsync(ApiKeyAuthenticationHandler.SchemeName);

        Assert.NotNull(scheme);
        Assert.Equal(typeof(ApiKeyAuthenticationHandler), scheme.HandlerType);
    }

    [Fact]
    public async Task ApiKey_FallsBackToEnvironmentVariable_WhenNotConfiguredInAppSettings()
    {
        // Sección 3 del patch: preparar el mecanismo para usar variables de entorno --
        // mismo criterio que OpenAIOptions.ApiKey.
        Environment.SetEnvironmentVariable(ApiAuthenticationServiceCollectionExtensions.ApiKeyEnvironmentVariableName, "clave-desde-env");
        try
        {
            using var host = await CreateHostAsync(configuredApiKey: null, requireAuthorizationOnEndpoint: false);

            var options = host.Services.GetRequiredService<IOptions<ApiAuthenticationOptions>>();

            Assert.Equal("clave-desde-env", options.Value.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ApiAuthenticationServiceCollectionExtensions.ApiKeyEnvironmentVariableName, null);
        }
    }

    private static async Task<IHost> CreateHostAsync(string? configuredApiKey, bool requireAuthorizationOnEndpoint)
    {
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
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        var route = endpoints.MapGet("/ping", (HttpContext ctx) =>
                            Results.Ok(new PingResponse(ctx.User.Identity?.IsAuthenticated ?? false)));

                        if (requireAuthorizationOnEndpoint)
                            route.RequireAuthorization();
                    });
                });
            });

        return await hostBuilder.StartAsync();
    }

    private sealed record PingResponse(bool Authenticated);
}
