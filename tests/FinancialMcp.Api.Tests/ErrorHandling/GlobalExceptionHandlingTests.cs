using System.Net;
using System.Text.Json;
using FinancialSystem.Api.ErrorHandling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FinancialMcp.Api.Tests.ErrorHandling;

/// <summary>
/// Cubre el manejo global de excepciones agregado en el Patch 0064 (PATCH-015):
/// AddApiProblemDetails + app.UseExceptionHandler() (mecanismo estándar de ASP.NET
/// Core, sin middleware propio -- ver ApiProblemDetailsServiceCollectionExtensions).
///
/// Host mínimo propio (Microsoft.AspNetCore.TestHost) con la MISMA infraestructura que
/// Program.cs registra, más un endpoint de prueba que arroja una excepción a propósito
/// -- ningún endpoint real de FinancialMcp.Api lanza excepciones no controladas en su
/// camino normal, así que no hay forma de ejercitar este mecanismo contra un endpoint
/// de producción sin fabricar uno. Mismo criterio que ApiKeyAuthenticationTests
/// (Patch 0058) para el "/ping" de control. Program.cs requiere una conexión real a
/// PostgreSQL en su arranque, fuera del alcance de este patch.
/// </summary>
public class GlobalExceptionHandlingTests
{
    [Fact]
    public async Task UnhandledException_ReturnsStatus500()
    {
        using var host = await CreateHostAsync(environmentName: "Production");
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/throws");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task UnhandledException_ReturnsProblemDetailsShapedJson()
    {
        using var host = await CreateHostAsync(environmentName: "Production");
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/throws");
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;

        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(500, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task UnhandledException_InProduction_DoesNotExposeExceptionDetails()
    {
        using var host = await CreateHostAsync(environmentName: "Production");
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/throws");
        var raw = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(raw);
        var root = body.RootElement;

        Assert.False(root.TryGetProperty("exceptionType", out _));
        Assert.False(root.TryGetProperty("exceptionMessage", out _));
        Assert.False(root.TryGetProperty("stackTrace", out _));
        // Ninguna traza del tipo de excepción ni del mensaje interno (que incluye una
        // ruta de archivo local, ver el endpoint de prueba más abajo) debe aparecer en
        // ningún lado del body, ni siquiera fuera de los campos esperados.
        Assert.DoesNotContain(nameof(InvalidOperationException), raw);
        Assert.DoesNotContain("Colo", raw);
        Assert.DoesNotContain("at FinancialMcp.Api.Tests", raw);
    }

    [Fact]
    public async Task UnhandledException_InDevelopment_IncludesExceptionDetailsForDiagnostics()
    {
        using var host = await CreateHostAsync(environmentName: "Development");
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/throws");
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;

        Assert.Equal(typeof(InvalidOperationException).FullName, root.GetProperty("exceptionType").GetString());
        Assert.Contains("Colo", root.GetProperty("exceptionMessage").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("stackTrace").GetString()));
    }

    [Fact]
    public async Task SuccessfulEndpoint_IsUnaffectedByTheExceptionHandler()
    {
        // Ausencia de regresiones sobre respuestas 2xx (sección 4 del patch).
        using var host = await CreateHostAsync(environmentName: "Production");
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/ping");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"pong\"", body);
    }

    [Fact]
    public async Task ControlledBadRequestResponse_KeepsItsOwnFormat_UnaffectedByProblemDetails()
    {
        // AddProblemDetails()/UseExceptionHandler() no deben reescribir una respuesta de
        // error YA controlada por un endpoint (Results.BadRequest) -- eso no pasa por
        // ninguna excepción, así que el exception handler nunca interviene.
        using var host = await CreateHostAsync(environmentName: "Production");
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/bad-request");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("\"entrada invalida\"", raw);
        Assert.NotEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(raw);
    }

    private static async Task<IHost> CreateHostAsync(string environmentName)
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.UseEnvironment(environmentName);
                webHost.ConfigureServices((context, services) =>
                {
                    services.AddRouting();
                    services.AddApiProblemDetails(context.HostingEnvironment);
                });
                webHost.Configure(app =>
                {
                    app.UseExceptionHandler();
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/ping", () => Results.Ok("pong"));
                        endpoints.MapGet("/bad-request", () => Results.BadRequest("entrada invalida"));
                        // Mensaje deliberadamente "sensible" (ruta local) para poder verificar
                        // que nunca aparece en la respuesta en Production. Cast explícito a
                        // Func<IResult> porque un lambda cuyo cuerpo solo lanza no tiene tipo
                        // natural inferible al pasarlo como el Delegate que espera MapGet.
                        endpoints.MapGet("/throws", (Func<IResult>)(() =>
                            throw new InvalidOperationException(
                                "Fallo simulado leyendo C:\\Colo\\Programas\\FinancialMcp\\secreto.txt")));
                    });
                });
            });

        return await hostBuilder.StartAsync();
    }
}
