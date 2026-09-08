using System.Net;
using System.Net.Http.Json;
using FinancialSystem.Api.Authentication;
using FinancialSystem.Api.DTOs;
using FinancialSystem.Api.Endpoints;
using FinancialSystem.Application;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Dedupe;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FinancialMcp.Api.Tests.MovementReview;

/// <summary>
/// DEDUPE-017 (revisión funcional): POST /api/movement-review/classify debe
/// devolver 409 Conflict con un mensaje específico cuando el SourceId pertenece a
/// un IdentityGroupId (MovementIdentityLink) cuyo otro miembro físico ya tiene su
/// propio ClassifiedMovementItem -- antes de este cambio,
/// ClassifyMovementFailureReason.PartOfAlreadyClassifiedIdentityGroup no tenía case
/// en el switch de este endpoint y caía en el brazo genérico (Results.Problem, 500).
///
/// Se mapean las extensiones REALES de FinancialMcp.Api (MapMovementReviewEndpoints
/// -- código de producción, sin duplicarlo) sobre un host mínimo propio con
/// AppDbContext InMemory, mismo criterio que MasterDataProtectedEndpointsTests
/// (Patch 0060/PATCH-011): Program.cs requiere una conexión real a PostgreSQL en su
/// arranque, fuera del alcance de este cambio.
/// </summary>
public class MovementReviewEndpointsClassifyTests
{
    private const string ValidApiKey = "clave-secreta-de-prueba";

    [Fact]
    public async Task Classify_SourceIdConMovementIdentityLinkYMiembroYaClasificado_Devuelve409ConMensajeEsperado()
    {
        using var host = await CreateHostAsync(ValidApiKey);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ValidApiKey);

        Guid categoryId, bankStatementBId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var bankDate = new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc);

            var category = new Category { Name = "Test", DisplayName = "Test" };
            db.Categories.Add(category);
            categoryId = category.Id;

            // BankStatement A (ya clasificado) y B (el que se va a intentar clasificar
            // ahora) -- mismo evento económico real, vinculados por MovementIdentityLink
            // (simula lo que DedupeEngine.ApplyAsync ya dejó persistido).
            var bankStatementA = new BankStatement
            {
                Date = bankDate, Concept = "TRANSFERENCIA", Amount = 100m, Currency = "ARS",
                BankName = "BBVA", ExternalId = Guid.NewGuid().ToString(), ImportedAtUtc = bankDate,
            };
            var bankStatementB = new BankStatement
            {
                Date = bankDate, Concept = "TRANSFERENCIA", Amount = 100m, Currency = "ARS",
                BankName = "BBVA", ExternalId = Guid.NewGuid().ToString(), ImportedAtUtc = bankDate,
            };
            db.BankStatements.AddRange(bankStatementA, bankStatementB);
            bankStatementBId = bankStatementB.Id;

            var identityGroupId = Guid.NewGuid();
            db.MovementIdentityLinks.AddRange(
                new MovementIdentityLink
                {
                    IdentityGroupId = identityGroupId,
                    SourceEntityType = SourceEntityType.BankStatement,
                    SourceId = bankStatementA.Id,
                    Role = IdentityRole.Pendiente,
                    Classification = IdentityClassification.Fuerte,
                    Evidence = "Test: F+K+L, frecuencia=1",
                    CreatedAtUtc = bankDate,
                    CreatedBy = "DedupeEngine",
                },
                new MovementIdentityLink
                {
                    IdentityGroupId = identityGroupId,
                    SourceEntityType = SourceEntityType.BankStatement,
                    SourceId = bankStatementB.Id,
                    Role = IdentityRole.Liquidado,
                    Classification = IdentityClassification.Fuerte,
                    Evidence = "Test: F+K+L, frecuencia=1",
                    CreatedAtUtc = bankDate,
                    CreatedBy = "DedupeEngine",
                });

            // A ya fue clasificado -- siembra directa (bypass del handler), igual que
            // ClassifyMovementHandlerTests.SeedClassifiedMovementAsync.
            var classifiedMovement = new ClassifiedMovement
            {
                EffectiveDate = bankDate,
                TotalAmount = 100m,
                Currency = "ARS",
                Description = "Movimiento clasificado de prueba",
                MovementType = MovementType.Transfer,
                FinancialImpact = FinancialImpact.InternalMovement,
                CategoryId = categoryId,
                Status = ClassificationStatus.Reviewed,
                ProcessingSource = ProcessingSource.ManualReview,
                CreatedAt = bankDate,
                ProcessedAt = bankDate,
            };
            db.ClassifiedMovements.Add(classifiedMovement);
            db.ClassifiedMovementItems.Add(new ClassifiedMovementItem
            {
                ClassifiedMovementId = classifiedMovement.Id,
                ClassifiedMovement = classifiedMovement,
                SourceEntityType = SourceEntityType.BankStatement,
                SourceId = bankStatementA.Id,
                Role = MovementRole.Reference,
                OriginalAmount = 100m,
                OriginalDate = bankDate,
                OriginalDescription = "Movimiento clasificado de prueba",
                OriginalCurrency = "ARS",
            });

            await db.SaveChangesAsync();
        }

        // Intentar clasificar B (nunca clasificado individualmente todavía) debe
        // fallar con 409, no con el 500 genérico que devolvía antes de este cambio.
        var response = await client.PostAsJsonAsync(
            "/api/movement-review/classify",
            new ClassifyMovementRequest(
                SourceEntityType.BankStatement, bankStatementBId, categoryId,
                MovementType.Transfer, FinancialImpact.InternalMovement, null, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var message = await response.Content.ReadFromJsonAsync<string>();
        Assert.Equal(
            "Este movimiento ya pertenece a una identidad económica que fue clasificada anteriormente.",
            message);
    }

    private static async Task<IHost> CreateHostAsync(string configuredApiKey)
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
                        new KeyValuePair<string, string?>("ApiAuthentication:ApiKey", configuredApiKey)
                    ]);
                });
                webHost.ConfigureServices((context, services) =>
                {
                    services.AddRouting();
                    services.AddApiKeyAuthentication(context.Configuration);
                    services.AddApplication();

                    services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                    services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapMovementReviewEndpoints());
                });
            });

        return await hostBuilder.StartAsync();
    }
}
