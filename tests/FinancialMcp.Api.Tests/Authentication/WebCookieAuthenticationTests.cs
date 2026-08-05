using System.Net;
using System.Net.Http.Json;
using FinancialSystem.Api.Authentication;
using FinancialSystem.Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FinancialMcp.Api.Tests.Authentication;

/// <summary>
/// Cubre la infraestructura de autenticación agregada en el Patch 0067A: Cookie
/// Authentication para la UI web, coexistiendo con el esquema ApiKey ya existente
/// (Patch 0058) vía AddApiAuthentication (nuevo -- AddApiKeyAuthentication, usado por
/// los tests de Patches 0058-0071, queda sin tocar).
///
/// Host mínimo propio (Microsoft.AspNetCore.TestHost) con AddApiAuthentication +
/// MapAuthEndpoints (código de producción real, sin duplicarlo) más un endpoint de
/// prueba "/ping" protegido, mismo criterio que ApiKeyAuthenticationTests (Patch
/// 0058): Program.cs requiere una conexión real a PostgreSQL en su arranque, fuera
/// del alcance de este patch.
///
/// Manejo de cookies: TestServer no gestiona un cookie jar automático entre requests
/// del mismo HttpClient (a diferencia de un navegador real) -- cada test que necesita
/// "estar logueado" extrae manualmente el header Set-Cookie de la respuesta de login
/// y lo reenvía como header Cookie en la siguiente request, simulando exactamente lo
/// que un navegador real haría solo.
/// </summary>
public class WebCookieAuthenticationTests
{
    private const string ValidApiKey = "clave-api-de-prueba";
    private const string ValidPassword = "contraseña-de-prueba";

    [Fact]
    public async Task Login_WithCorrectPassword_ReturnsOkAndSetsSessionCookie()
    {
        using var host = await CreateHostAsync(apiKey: null, webPassword: ValidPassword);
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(ValidPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Login_WithIncorrectPassword_ReturnsUnauthorizedWithoutSettingCookie()
    {
        using var host = await CreateHostAsync(apiKey: null, webPassword: ValidPassword);
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("clave-incorrecta"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Login_WithNoPasswordConfigured_NeverAuthenticates()
    {
        // Mismo criterio que ApiKeyAuthenticationHandler.IsMatch para
        // ApiAuthenticationOptions.ApiKey: una contraseña ausente en la configuración
        // no debe degradar en "aceptar cualquier cosa".
        using var host = await CreateHostAsync(apiKey: null, webPassword: string.Empty);
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("cualquier-valor"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithValidSessionCookie_Succeeds()
    {
        using var host = await CreateHostAsync(apiKey: null, webPassword: ValidPassword);
        using var client = host.GetTestClient();
        var cookie = await LoginAndGetCookieAsync(client);

        var response = await SendWithCookieAsync(client, HttpMethod.Get, "/ping", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Session_WithValidCookie_ReturnsOk()
    {
        using var host = await CreateHostAsync(apiKey: null, webPassword: ValidPassword);
        using var client = host.GetTestClient();
        var cookie = await LoginAndGetCookieAsync(client);

        var response = await SendWithCookieAsync(client, HttpMethod.Get, "/api/auth/session", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Session_WithoutAnyCredential_ReturnsUnauthorized()
    {
        using var host = await CreateHostAsync(apiKey: ValidApiKey, webPassword: ValidPassword);
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/api/auth/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_ReturnsOkAndInstructsTheBrowserToClearTheCookie()
    {
        using var host = await CreateHostAsync(apiKey: null, webPassword: ValidPassword);
        using var client = host.GetTestClient();
        var cookie = await LoginAndGetCookieAsync(client);

        var response = await SendWithCookieAsync(client, HttpMethod.Post, "/api/auth/logout", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Logout_WithoutAnyExistingSession_IsANoOpSuccess()
    {
        // Logout es idempotente a propósito: cerrar sesión sin tener una sesión
        // válida no debe ser un error.
        using var host = await CreateHostAsync(apiKey: null, webPassword: ValidPassword);
        using var client = host.GetTestClient();

        var response = await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Logout_ThenAccessingProtectedEndpointWithoutResendingTheCookie_Fails()
    {
        // La autenticación por cookie acá es sin estado (sin ticket store del lado
        // del servidor) -- "cerrar sesión" es el servidor indicándole al navegador que
        // borre la cookie (Set-Cookie con expiración en el pasado), no una lista de
        // revocación server-side. Este test simula el comportamiento correcto de un
        // navegador real: después de logout, ya no reenvía ninguna cookie.
        using var host = await CreateHostAsync(apiKey: null, webPassword: ValidPassword);
        using var client = host.GetTestClient();
        var cookie = await LoginAndGetCookieAsync(client);

        var logoutResponse = await SendWithCookieAsync(client, HttpMethod.Post, "/api/auth/logout", cookie);
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        var pingAfterLogout = await client.GetAsync("/ping");

        Assert.Equal(HttpStatusCode.Unauthorized, pingAfterLogout.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithValidApiKey_StillSucceeds()
    {
        // Ausencia de regresión (sección 6 del patch): el mecanismo de API Key sigue
        // completamente operativo, sin cambios.
        using var host = await CreateHostAsync(apiKey: ValidApiKey, webPassword: null);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        var response = await client.GetAsync("/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithNeitherCookieNorApiKey_ReturnsUnauthorized()
    {
        using var host = await CreateHostAsync(apiKey: ValidApiKey, webPassword: ValidPassword);
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/ping");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BothMechanisms_CoexistOnTheSameHost_IndependentlyOfEachOther()
    {
        // Convivencia de ambos mecanismos (sección de tests del patch): en el mismo
        // host, con la misma configuración, ambos esquemas funcionan cada uno por su
        // cuenta -- ninguno reemplaza ni interfiere con el otro.
        using var host = await CreateHostAsync(apiKey: ValidApiKey, webPassword: ValidPassword);
        using var client = host.GetTestClient();

        var apiKeyResponse = await SendWithApiKeyAsync(client, HttpMethod.Get, "/ping", ValidApiKey);
        Assert.Equal(HttpStatusCode.OK, apiKeyResponse.StatusCode);

        var cookie = await LoginAndGetCookieAsync(client);
        var cookieResponse = await SendWithCookieAsync(client, HttpMethod.Get, "/ping", cookie);
        Assert.Equal(HttpStatusCode.OK, cookieResponse.StatusCode);

        var anonymousResponse = await client.GetAsync("/ping");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<string> LoginAndGetCookieAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(ValidPassword));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ExtractCookie(response);
    }

    private static string ExtractCookie(HttpResponseMessage response)
    {
        var setCookie = response.Headers.GetValues("Set-Cookie").First();
        // Solo interesa el segmento "nombre=valor" -- el resto (path, expires,
        // httponly, samesite) son atributos de la cookie, no parte del header Cookie
        // que se manda de vuelta.
        return setCookie.Split(';')[0];
    }

    private static Task<HttpResponseMessage> SendWithCookieAsync(
        HttpClient client, HttpMethod method, string path, string cookie)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", cookie);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendWithApiKeyAsync(
        HttpClient client, HttpMethod method, string path, string apiKey)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(ApiKeyAuthenticationHandler.HeaderName, apiKey);
        return client.SendAsync(request);
    }

    private static async Task<IHost> CreateHostAsync(string? apiKey, string? webPassword)
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureAppConfiguration(config =>
                {
                    var settings = new List<KeyValuePair<string, string?>>();
                    if (apiKey is not null)
                        settings.Add(new KeyValuePair<string, string?>("ApiAuthentication:ApiKey", apiKey));
                    if (webPassword is not null)
                        settings.Add(new KeyValuePair<string, string?>("WebAuthentication:Password", webPassword));

                    config.AddInMemoryCollection(settings);
                });
                webHost.ConfigureServices((context, services) =>
                {
                    services.AddRouting();
                    services.AddApiAuthentication(context.Configuration);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapAuthEndpoints();
                        endpoints.MapGet("/ping", () => Results.Ok("pong")).RequireAuthorization();
                    });
                });
            });

        return await hostBuilder.StartAsync();
    }
}
