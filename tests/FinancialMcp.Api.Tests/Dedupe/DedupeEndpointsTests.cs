using System.Net;
using System.Net.Http.Json;
using FinancialSystem.Api.Authentication;
using FinancialSystem.Api.DTOs;
using FinancialSystem.Api.Endpoints;
using FinancialSystem.Application;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Dedupe;
using FinancialSystem.Domain.Dedupe;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FinancialMcp.Api.Tests.Dedupe;

/// <summary>
/// Cobertura de PATCH-0112 (POST /api/dedupe/apply) -- único punto de producción que
/// invoca IDedupeEngine.ApplyAsync (ver doc-comment de DedupeEndpoints).
///
/// La mayoría de estos tests registran el DedupeEngine REAL (FinancialSystem.Infrastructure
/// .Dedupe.DedupeEngine, internal -- visible acá vía InternalsVisibleTo, ver AssemblyInfo.cs
/// de FinancialSystem.Infrastructure, mismo criterio ya usado por
/// MovementLookupServiceRealEfRegressionTests en McpServer.Tests), no un fake: el objetivo
/// es probar el contrato completo "bankStatementIds -> PreviewAsync real -> Fuerte ->
/// ApplyAsync real" tal como corre en producción, incluida la asimetría real de focusIds
/// entre vía B y el pipeline principal (ver revisión pre-implementación de PATCH-0112) --
/// no una reimplementación ni un doble de prueba del motor. La única excepción es el test
/// de candidato ambiguo (Test 7): esa situación es estructuralmente imposible de producir
/// con el motor real (DegradarConflictosDeIdentidadFisica lo impide por diseño), así que
/// ahí se usa un FakeDedupeEngine para ejercitar el código defensivo del endpoint en
/// aislamiento.
///
/// Mismo criterio de host mínimo que MasterDataProtectedEndpointsTests/
/// PlanningAuditInvestigationsProtectedEndpointsTests (Program.cs requiere una conexión
/// real a PostgreSQL en su arranque, fuera del alcance de este patch): se mapea la
/// extensión REAL MapDedupeEndpoints() sobre un host propio.
/// </summary>
public class DedupeEndpointsTests
{
    private const string ValidApiKey = "clave-secreta-de-prueba";
    private static readonly Guid Account = Guid.NewGuid();

    private sealed class FakeDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc);
    }

    // ── Host con el DedupeEngine REAL ────────────────────────────────────────

    private static async Task<IHost> CreateHostAsync(string dbName) =>
        await CreateHostAsync(dbName, fakeDedupeEngine: null);

    // ── Host con un IDedupeEngine sustituido (solo para el caso de ambigüedad,
    // estructuralmente imposible de producir con el motor real -- ver doc-comment) ──

    private static async Task<IHost> CreateHostWithEngineAsync(IDedupeEngine fakeDedupeEngine) =>
        await CreateHostAsync(dbName: Guid.NewGuid().ToString(), fakeDedupeEngine);

    private static async Task<IHost> CreateHostAsync(string dbName, IDedupeEngine? fakeDedupeEngine)
    {
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
                    services.AddApplication();

                    services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                    services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

                    if (fakeDedupeEngine is not null)
                        services.AddSingleton(fakeDedupeEngine);
                    else
                        services.AddScoped<IDedupeEngine, FinancialSystem.Infrastructure.Dedupe.DedupeEngine>();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapDedupeEndpoints());
                });
            });

        return await hostBuilder.StartAsync();
    }

    private static HttpClient AuthedClient(IHost host)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);
        return client;
    }

    // ── Datos de prueba ───────────────────────────────────────────────────────

    private static BankStatement Bs(
        DateTime date, decimal amount, string concept, string sourceFile, int rowNumber,
        [System.Runtime.CompilerServices.CallerMemberName] string externalIdSeed = "") => new()
    {
        Id = Guid.NewGuid(),
        Date = date,
        Amount = amount,
        Concept = concept,
        SourceFile = sourceFile,
        RowNumber = rowNumber,
        BankName = "BBVA",
        Currency = "ARS",
        ExternalId = $"{externalIdSeed}-{sourceFile}-{rowNumber}-{Guid.NewGuid():N}",
        ImportedAtUtc = DateTime.UtcNow,
        FinancialAccountId = Account,
    };

    private static async Task SeedAsync(string dbName, params BankStatement[] statements)
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);
        db.BankStatements.AddRange(statements);
        await db.SaveChangesAsync();
    }

    // Par vía B (duplicado exacto): mismo Date+Concept+Amount, distinto SourceFile --
    // reproduce el caso real de esta investigación (03b23f92.../5a9c18a5...). Vía B no
    // depende de roleOk/forma Nro, así que es el caso más simple y determinístico para
    // probar el contrato del endpoint sin acoplar los tests a las demás señales.
    private static async Task<(BankStatement A, BankStatement B)> SeedViaBPairAsync(string dbName)
    {
        var a = Bs(new DateTime(2026, 7, 12), -16555.00m, "PAGO CON VISA DEBITO 96477108 OP1482", "archivo1.xls", 1);
        var b = Bs(new DateTime(2026, 7, 12), -16555.00m, "PAGO CON VISA DEBITO 96477108 OP1482", "archivo2.xls", 1);
        await SeedAsync(dbName, a, b);
        return (a, b);
    }

    // ── Autorización (mismo patrón que el resto de la API) ──────────────────────

    [Fact]
    public async Task Apply_SinApiKey_Retorna401()
    {
        var dbName = nameof(Apply_SinApiKey_Retorna401);
        var (a, b) = await SeedViaBPairAsync(dbName);
        using var host = await CreateHostAsync(dbName);
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/dedupe/apply", new DedupeApplyRequest([a.Id, b.Id]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Test 1 — aplicación normal ───────────────────────────────────────────

    [Fact]
    public async Task Apply_ConParFuerteReal_CreaUnGrupoYVinculaAmbosMiembros()
    {
        var dbName = nameof(Apply_ConParFuerteReal_CreaUnGrupoYVinculaAmbosMiembros);
        var (a, b) = await SeedViaBPairAsync(dbName);
        using var host = await CreateHostAsync(dbName);
        using var client = AuthedClient(host);

        var response = await client.PostAsJsonAsync(
            "/api/dedupe/apply", new DedupeApplyRequest([a.Id, b.Id]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DedupeApplyResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(1, body!.GroupsCreated);
        Assert.Empty(body.Skipped);

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);
        var links = await db.MovementIdentityLinks.ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.Contains(links, l => l.SourceId == a.Id);
        Assert.Contains(links, l => l.SourceId == b.Id);
        Assert.Equal(links[0].IdentityGroupId, links[1].IdentityGroupId);
    }

    // ── Test 2 — repetición (idempotencia) ───────────────────────────────────

    [Fact]
    public async Task Apply_RepetidoConLosMismosIds_NoCreaUnSegundoGrupo()
    {
        var dbName = nameof(Apply_RepetidoConLosMismosIds_NoCreaUnSegundoGrupo);
        var (a, b) = await SeedViaBPairAsync(dbName);
        using var host = await CreateHostAsync(dbName);
        using var client = AuthedClient(host);
        var request = new DedupeApplyRequest([a.Id, b.Id]);

        var firstResponse = await client.PostAsJsonAsync("/api/dedupe/apply", request);
        var secondResponse = await client.PostAsJsonAsync("/api/dedupe/apply", request);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var firstBody = await firstResponse.Content.ReadFromJsonAsync<DedupeApplyResponseDto>();
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<DedupeApplyResponseDto>();
        Assert.Equal(1, firstBody!.GroupsCreated);
        Assert.Equal(0, secondBody!.GroupsCreated);

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);
        Assert.Equal(2, (await db.MovementIdentityLinks.ToListAsync()).Count); // no un segundo grupo
    }

    // ── Test 3 — orden inverso ────────────────────────────────────────────────

    [Fact]
    public async Task Apply_ConIdsEnOrdenInverso_ProduceElMismoResultado()
    {
        var dbNameDirecto = nameof(Apply_ConIdsEnOrdenInverso_ProduceElMismoResultado) + "-directo";
        var (a1, b1) = await SeedViaBPairAsync(dbNameDirecto);
        using var hostDirecto = await CreateHostAsync(dbNameDirecto);
        using var clientDirecto = AuthedClient(hostDirecto);
        var responseDirecto = await clientDirecto.PostAsJsonAsync(
            "/api/dedupe/apply", new DedupeApplyRequest([a1.Id, b1.Id]));

        var dbNameInverso = nameof(Apply_ConIdsEnOrdenInverso_ProduceElMismoResultado) + "-inverso";
        var (a2, b2) = await SeedViaBPairAsync(dbNameInverso);
        using var hostInverso = await CreateHostAsync(dbNameInverso);
        using var clientInverso = AuthedClient(hostInverso);
        var responseInverso = await clientInverso.PostAsJsonAsync(
            "/api/dedupe/apply", new DedupeApplyRequest([b2.Id, a2.Id])); // orden invertido

        Assert.Equal(HttpStatusCode.OK, responseDirecto.StatusCode);
        Assert.Equal(HttpStatusCode.OK, responseInverso.StatusCode);

        var bodyDirecto = await responseDirecto.Content.ReadFromJsonAsync<DedupeApplyResponseDto>();
        var bodyInverso = await responseInverso.Content.ReadFromJsonAsync<DedupeApplyResponseDto>();
        Assert.Equal(bodyDirecto!.GroupsCreated, bodyInverso!.GroupsCreated);
        Assert.Empty(bodyDirecto.Skipped);
        Assert.Empty(bodyInverso.Skipped);
    }

    // ── Test 4 — vía B: [A], [B], [A,B] reconstruyen el mismo candidato ─────────
    // (contrato real de PreviewAsync -- ver revisión pre-implementación de PATCH-0112;
    // el camino OFICIAL del endpoint sigue siendo enviar la lista completa, probado en
    // el Test 1 de arriba.)

    [Fact]
    public async Task PreviewAsync_ConSoloUnoDeLosDosIds_OConAmbos_ReconstruyeElMismoCandidatoViaB()
    {
        var dbName = nameof(PreviewAsync_ConSoloUnoDeLosDosIds_OConAmbos_ReconstruyeElMismoCandidatoViaB);
        var (a, b) = await SeedViaBPairAsync(dbName);

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);
        var engine = new FinancialSystem.Infrastructure.Dedupe.DedupeEngine(db, new FakeDateTimeProvider());

        var soloA = await engine.PreviewAsync([a.Id]);
        var soloB = await engine.PreviewAsync([b.Id]);
        var ambos = await engine.PreviewAsync([a.Id, b.Id]);

        foreach (var resultados in new[] { soloA, soloB, ambos })
        {
            var candidato = Assert.Single(resultados);
            Assert.Equal(IdentityClassification.Fuerte, candidato.Classification);

            // Vía B asigna Pendiente/Liquidado por orden canónico de GUID
            // (DedupeEngine.Evaluate: "ordenados = miembros.OrderBy(m => m.Statement.Id)"),
            // no por el orden en que se sembraron a/b -- Guid.NewGuid() no tiene relación
            // con ese orden, así que afirmar qué lado ocupa cada rol es no determinista.
            // Lo que este test debe probar es que [A], [B] y [A,B] reconstruyen el mismo
            // par físico, no qué GUID cae en cada rol.
            Assert.Equal(
                new HashSet<Guid> { a.Id, b.Id },
                new HashSet<Guid> { candidato.PendienteId, candidato.LiquidadoId });
        }
    }

    // ── Test 5 — candidato inexistente ───────────────────────────────────────

    [Fact]
    public async Task Apply_ConIdsQueNoExisten_Retorna404YNoAplicaNada()
    {
        var dbName = nameof(Apply_ConIdsQueNoExisten_Retorna404YNoAplicaNada);
        using var host = await CreateHostAsync(dbName); // sin seed -- IDs inexistentes
        using var client = AuthedClient(host);

        var response = await client.PostAsJsonAsync(
            "/api/dedupe/apply", new DedupeApplyRequest([Guid.NewGuid(), Guid.NewGuid()]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);
        Assert.Empty(await db.MovementIdentityLinks.ToListAsync());
    }

    // ── Test 6 — candidato encontrado pero no FUERTE ─────────────────────────

    [Fact]
    public async Task Apply_ConParPosible_NoLlamaApplyAsyncYRetorna404()
    {
        var dbName = nameof(Apply_ConParPosible_NoLlamaApplyAsyncYRetorna404);
        // Mismo par "Posible" usado en DedupeEngineTests.ApplyAsync_PersisteSoloFuerte_
        // NuncaPosibleNiIndeterminado: TRANSF DEBITO Nro:.../TRANSFERENCIA sin transformación
        // validada -- Posible, no Fuerte.
        var pendiente = Bs(new DateTime(2026, 8, 1), -44444.00m, "TRANSF DEBITO Nro:400444", "archivo1.xls", 3);
        var liquidado = Bs(new DateTime(2026, 8, 3), -44444.00m, "TRANSFERENCIA", "archivo2.xls", 3);
        await SeedAsync(dbName, pendiente, liquidado);
        using var host = await CreateHostAsync(dbName);
        using var client = AuthedClient(host);

        var response = await client.PostAsJsonAsync(
            "/api/dedupe/apply", new DedupeApplyRequest([pendiente.Id, liquidado.Id]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);
        Assert.Empty(await db.MovementIdentityLinks.ToListAsync());
    }

    // ── Test 7 — candidato ambiguo (estructuralmente imposible con el motor real;
    // se prueba el código defensivo del endpoint con un IDedupeEngine sustituido) ────

    private sealed class FakeAmbiguousDedupeEngine : IDedupeEngine
    {
        private readonly Guid _pendienteId;
        private readonly Guid _liquidadoId;

        public FakeAmbiguousDedupeEngine(Guid pendienteId, Guid liquidadoId)
        {
            _pendienteId = pendienteId;
            _liquidadoId = liquidadoId;
        }

        public Task<IReadOnlyList<DedupeCandidateResult>> PreviewAsync(
            IReadOnlyList<Guid>? focusBankStatementIds = null, CancellationToken cancellationToken = default)
        {
            // Dos resultados Fuerte DISTINTOS (evidencia distinta) pero con el mismo
            // conjunto exacto de miembros físicos -- situación que Evaluate() nunca
            // produce en la implementación real (DegradarConflictosDeIdentidadFisica lo
            // impide), pero que el endpoint debe rechazar igual si alguna vez llegara.
            IReadOnlyList<DedupeCandidateResult> resultados =
            [
                new DedupeCandidateResult(_pendienteId, "concepto", DateTime.UtcNow, -1m, "archivo1.xls",
                    _liquidadoId, "concepto", DateTime.UtcNow, -1m, "archivo2.xls",
                    IdentityClassification.Fuerte, "evidencia A", []),
                new DedupeCandidateResult(_pendienteId, "concepto", DateTime.UtcNow, -1m, "archivo1.xls",
                    _liquidadoId, "concepto", DateTime.UtcNow, -1m, "archivo2.xls",
                    IdentityClassification.Fuerte, "evidencia B (fabricada, distinta de A)", []),
            ];
            return Task.FromResult(resultados);
        }

        public Task<ApplyOutcome> ApplyAsync(
            IReadOnlyList<DedupeCandidateResult> results, string createdBy,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "No debería llamarse ApplyAsync ante un candidato ambiguo -- si este método se " +
                "invoca, el endpoint eligió arbitrariamente y el test debe fallar.");
    }

    [Fact]
    public async Task Apply_ConDosCandidatosFuerteParaElMismoConjuntoDeIds_NoAplicaNingunoYRetornaConflict()
    {
        var pendienteId = Guid.NewGuid();
        var liquidadoId = Guid.NewGuid();
        using var host = await CreateHostWithEngineAsync(new FakeAmbiguousDedupeEngine(pendienteId, liquidadoId));
        using var client = AuthedClient(host);

        var response = await client.PostAsJsonAsync(
            "/api/dedupe/apply", new DedupeApplyRequest([pendienteId, liquidadoId]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── Test 8 — miembro ya vinculado: el Detail no se pierde ───────────────────

    [Fact]
    public async Task Apply_ConMiembroYaVinculado_ExponeElDetailDelSkip()
    {
        var dbName = nameof(Apply_ConMiembroYaVinculado_ExponeElDetailDelSkip);
        var (a, b) = await SeedViaBPairAsync(dbName);
        using var host = await CreateHostAsync(dbName);
        using var client = AuthedClient(host);
        var request = new DedupeApplyRequest([a.Id, b.Id]);

        await client.PostAsJsonAsync("/api/dedupe/apply", request); // primera aplicación
        var response = await client.PostAsJsonAsync("/api/dedupe/apply", request); // repetida

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DedupeApplyResponseDto>();
        var skip = Assert.Single(body!.Skipped);
        Assert.Equal(nameof(ApplySkipReason.YaAplicado), skip.Reason);
        Assert.False(string.IsNullOrWhiteSpace(skip.Detail)); // el Detail real de ApplyAsync, no perdido
        Assert.Contains("MovementIdentityLink", skip.Detail);
    }

    // ── Test 9 — IDs duplicados en el request ────────────────────────────────
    // Convención elegida: rechazar con 400 -- un candidato real nunca tiene el mismo
    // BankStatement.Id repetido dos veces entre sus miembros, así que un duplicado en
    // el request es, en el mejor de los casos, ruido del cliente y, en el peor, un
    // intento de eludir la validación de "al menos 2 miembros distintos".

    [Fact]
    public async Task Apply_ConIdsDuplicadosEnElRequest_Retorna400()
    {
        var dbName = nameof(Apply_ConIdsDuplicadosEnElRequest_Retorna400);
        var (a, _) = await SeedViaBPairAsync(dbName);
        using var host = await CreateHostAsync(dbName);
        using var client = AuthedClient(host);

        var response = await client.PostAsJsonAsync(
            "/api/dedupe/apply", new DedupeApplyRequest([a.Id, a.Id]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Validaciones adicionales de request (nulo / vacío / insuficiente) ───────

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Apply_ConRequestInvalido_Retorna400(IReadOnlyList<Guid>? bankStatementIds)
    {
        var dbName = nameof(Apply_ConRequestInvalido_Retorna400) + Guid.NewGuid();
        using var host = await CreateHostAsync(dbName);
        using var client = AuthedClient(host);

        var response = await client.PostAsJsonAsync(
            "/api/dedupe/apply", new DedupeApplyRequest(bankStatementIds));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public static IEnumerable<object?[]> InvalidRequests()
    {
        yield return [null];
        yield return [Array.Empty<Guid>()];
        yield return [new[] { Guid.NewGuid() }]; // un solo Id -- insuficiente para un par
    }
}
