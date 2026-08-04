using System.Net;
using System.Net.Http.Headers;
using FinancialSystem.Api.Authentication;
using FinancialSystem.Api.Endpoints;
using FinancialSystem.Api.Imports;
using FinancialSystem.Application.Imports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FinancialMcp.Api.Tests.Imports;

/// <summary>
/// Cubre las validaciones agregadas a POST /api/imports en el Patch 0063 (PATCH-014):
/// límite de tamaño explícito, extensión soportada y "magic bytes" coherentes con la
/// extensión declarada -- todas antes de tocar disco o invocar IFileImportRouter (ver
/// ImportBatchEndpoints.Upload). No cubre IHttpMaxRequestBodySizeFeature (el límite de
/// Kestrel scopeado a esta ruta en Program.cs): TestServer no simula un transporte
/// Kestrel real y esa feature no está presente en su FeatureCollection -- el límite de
/// negocio que sí importa para estos tests (file.Length > MaxFileSizeBytes) se aplica
/// en el propio handler y es lo que se prueba acá.
///
/// Se mapea la extensión REAL de FinancialMcp.Api (MapImportBatchEndpoints -- código de
/// producción, sin duplicarlo) sobre un host mínimo propio, mismo criterio que
/// ProtectedEndpointsTests (Patch 0059): Program.cs requiere una conexión real a
/// PostgreSQL en su arranque, fuera del alcance de este patch.
/// </summary>
public class ImportUploadValidationTests
{
    private const string ValidApiKey = "clave-secreta-de-prueba";
    private const string UploadPath = "/api/imports";

    [Fact]
    public async Task Upload_WithValidFile_ReachesTheRouterAndReturnsOk()
    {
        using var host = await CreateHostAsync(maxFileSizeBytes: null);
        using var client = AuthorizedClient(host);
        var router = host.Services.GetRequiredService<RecordingFileImportRouter>();

        using var content = BuildMultipartContent("fecha,monto\n2026-01-01,100"u8.ToArray(), "extracto.csv");
        var response = await client.PostAsync(UploadPath, content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, router.CallCount);
    }

    [Fact]
    public async Task Upload_WithEmptyFile_ReturnsBadRequestWithoutReachingTheRouter()
    {
        using var host = await CreateHostAsync(maxFileSizeBytes: null);
        using var client = AuthorizedClient(host);
        var router = host.Services.GetRequiredService<RecordingFileImportRouter>();

        using var content = BuildMultipartContent([], "extracto.csv");
        var response = await client.PostAsync(UploadPath, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, router.CallCount);
    }

    [Fact]
    public async Task Upload_ExceedingConfiguredMaxSize_ReturnsBadRequestWithoutReachingTheRouter()
    {
        // Límite bajo a propósito para no tener que armar un archivo real de varios MB.
        using var host = await CreateHostAsync(maxFileSizeBytes: 10);
        using var client = AuthorizedClient(host);
        var router = host.Services.GetRequiredService<RecordingFileImportRouter>();

        using var content = BuildMultipartContent("fecha,monto\n2026-01-01,100"u8.ToArray(), "extracto.csv");
        var response = await client.PostAsync(UploadPath, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, router.CallCount);
    }

    [Fact]
    public async Task Upload_WithUnsupportedExtension_ReturnsBadRequestWithoutReachingTheRouter()
    {
        using var host = await CreateHostAsync(maxFileSizeBytes: null);
        using var client = AuthorizedClient(host);
        var router = host.Services.GetRequiredService<RecordingFileImportRouter>();

        using var content = BuildMultipartContent("contenido"u8.ToArray(), "extracto.exe");
        var response = await client.PostAsync(UploadPath, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, router.CallCount);
    }

    [Fact]
    public async Task Upload_WithContentThatDoesNotMatchExtension_ReturnsBadRequestWithoutReachingTheRouter()
    {
        // Extensión .pdf declarada, pero el contenido no arranca con la firma "%PDF".
        using var host = await CreateHostAsync(maxFileSizeBytes: null);
        using var client = AuthorizedClient(host);
        var router = host.Services.GetRequiredService<RecordingFileImportRouter>();

        using var content = BuildMultipartContent("esto no es un pdf real"u8.ToArray(), "extracto.pdf");
        var response = await client.PostAsync(UploadPath, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, router.CallCount);
    }

    [Fact]
    public async Task Upload_WithoutApiKey_ReturnsUnauthorized()
    {
        // Ausencia de regresión sobre el Patch 0059: el grupo sigue protegido.
        using var host = await CreateHostAsync(maxFileSizeBytes: null);
        using var client = host.GetTestClient();

        using var content = BuildMultipartContent("fecha,monto\n2026-01-01,100"u8.ToArray(), "extracto.csv");
        var response = await client.PostAsync(UploadPath, content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static HttpClient AuthorizedClient(IHost host)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);
        return client;
    }

    private static MultipartFormDataContent BuildMultipartContent(byte[] fileContent, string fileName)
    {
        var multipart = new MultipartFormDataContent();
        var part = new ByteArrayContent(fileContent);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(part, "file", fileName);
        return multipart;
    }

    private static async Task<IHost> CreateHostAsync(long? maxFileSizeBytes)
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureAppConfiguration(config =>
                {
                    var settings = new List<KeyValuePair<string, string?>>
                    {
                        new("ApiAuthentication:ApiKey", ValidApiKey)
                    };
                    if (maxFileSizeBytes is { } max)
                        settings.Add(new KeyValuePair<string, string?>("ImportUpload:MaxFileSizeBytes", max.ToString()));

                    config.AddInMemoryCollection(settings);
                });
                webHost.ConfigureServices((context, services) =>
                {
                    services.AddRouting();
                    services.AddApiKeyAuthentication(context.Configuration);
                    services.Configure<ImportUploadOptions>(context.Configuration.GetSection(ImportUploadOptions.SectionName));

                    services.AddSingleton<RecordingFileImportRouter>();
                    services.AddSingleton<IFileImportRouter>(sp => sp.GetRequiredService<RecordingFileImportRouter>());
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapImportBatchEndpoints());
                });
            });

        return await hostBuilder.StartAsync();
    }

    private sealed class RecordingFileImportRouter : IFileImportRouter
    {
        public int CallCount { get; private set; }

        public Task RouteAsync(string filePath, CancellationToken ct = default)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }
}
