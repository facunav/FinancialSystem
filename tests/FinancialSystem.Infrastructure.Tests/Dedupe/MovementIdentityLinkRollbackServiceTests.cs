using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Dedupe;
using FinancialSystem.Domain.Dedupe;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Infrastructure.Dedupe;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Dedupe;

/// <summary>
/// Cobertura de <see cref="MovementIdentityLinkRollbackService"/> -- DEDUPE-010. Servicio
/// deliberadamente separado de DedupeEngine: estos tests seedean MovementIdentityLink
/// directamente (no pasan por Evaluate/PreviewAsync/ApplyAsync), porque el servicio no
/// depende de ninguna señal de clasificación -- solo opera sobre lo que ya existe en
/// MovementIdentityLinks.
///
/// Mismo patrón de AppDbContext InMemory que DedupeEngineTests: cada paso abre su propio
/// contexto sobre el mismo nombre de base.
/// </summary>
public class MovementIdentityLinkRollbackServiceTests
{
    private static readonly DateTime FixedNow = new(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => FixedNow;
    }

    private static AppDbContext OpenDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private static MovementIdentityLinkRollbackService NewService(AppDbContext db) =>
        new(db, new FakeDateTimeProvider());

    // Construye un grupo FUERTE ya aplicado (como si ApplyAsync ya hubiera corrido) --
    // sin pasar por DedupeEngine, porque el servicio de rollback no lo necesita.
    private static List<MovementIdentityLink> BuildGroup(
        Guid groupId, int memberCount = 2, string createdBy = "DedupeEngine",
        DateTime? createdAtUtc = null)
    {
        var roles = new[] { IdentityRole.Pendiente, IdentityRole.Liquidado, IdentityRole.CarryForward, IdentityRole.CarryForward };
        var links = new List<MovementIdentityLink>();
        for (var i = 0; i < memberCount; i++)
        {
            links.Add(new MovementIdentityLink
            {
                Id = Guid.NewGuid(),
                IdentityGroupId = groupId,
                SourceEntityType = SourceEntityType.BankStatement,
                SourceId = Guid.NewGuid(),
                Role = roles[i],
                Classification = IdentityClassification.Fuerte,
                Evidence = $"evidencia de prueba miembro {i}",
                CreatedAtUtc = createdAtUtc ?? FixedNow.AddDays(-1),
                CreatedBy = createdBy,
            });
        }
        return links;
    }

    private static async Task SeedAsync(string dbName, params MovementIdentityLink[] links)
    {
        await using var db = OpenDb(dbName);
        db.MovementIdentityLinks.AddRange(links);
        await db.SaveChangesAsync();
    }

    // ── Test 1 / 9 — rollback de grupo existente, elimina todos los links ───────

    [Fact]
    public async Task RollbackAsync_ConGrupoExistente_EliminaTodosLosLinksYRegistraAuditoria()
    {
        var dbName = nameof(RollbackAsync_ConGrupoExistente_EliminaTodosLosLinksYRegistraAuditoria);
        var groupId = Guid.NewGuid();
        var links = BuildGroup(groupId, memberCount: 2);
        await SeedAsync(dbName, links.ToArray());

        RollbackResult result;
        await using (var db = OpenDb(dbName))
        {
            result = await NewService(db).RollbackAsync(groupId, "facunav", "aplicado por error", default);
        }

        Assert.Equal(RollbackOutcome.RolledBack, result.Outcome);
        Assert.Equal(groupId, result.IdentityGroupId);
        Assert.Equal(2, result.MembersAffected);

        await using var verifyDb = OpenDb(dbName);
        Assert.Empty(await verifyDb.MovementIdentityLinks.Where(l => l.IdentityGroupId == groupId).ToListAsync());

        var rollback = await verifyDb.MovementIdentityLinkRollbacks.SingleAsync(r => r.IdentityGroupId == groupId);
        Assert.Equal("facunav", rollback.RolledBackBy);
        Assert.Equal("aplicado por error", rollback.Reason);
        Assert.Equal(FixedNow, rollback.RolledBackAtUtc);

        var members = await verifyDb.MovementIdentityLinkRollbackMembers
            .Where(m => m.RollbackId == rollback.Id).ToListAsync();
        Assert.Equal(2, members.Count);
    }

    // ── Test 2 — rollback de grupo con CarryForward (3+ miembros) ────────────────

    [Fact]
    public async Task RollbackAsync_ConCarryForward_RevierteLosTresMiembros()
    {
        var dbName = nameof(RollbackAsync_ConCarryForward_RevierteLosTresMiembros);
        var groupId = Guid.NewGuid();
        var links = BuildGroup(groupId, memberCount: 3);
        await SeedAsync(dbName, links.ToArray());

        await using var db = OpenDb(dbName);
        var result = await NewService(db).RollbackAsync(groupId, "facunav", "grupo con carry-forward incorrecto", default);

        Assert.Equal(RollbackOutcome.RolledBack, result.Outcome);
        Assert.Equal(3, result.MembersAffected);
        Assert.Empty(await db.MovementIdentityLinks.Where(l => l.IdentityGroupId == groupId).ToListAsync());
        Assert.Equal(3, await db.MovementIdentityLinkRollbackMembers.CountAsync());

        var roles = (await db.MovementIdentityLinkRollbackMembers.ToListAsync()).Select(m => m.Role).ToHashSet();
        Assert.Contains(IdentityRole.Pendiente, roles);
        Assert.Contains(IdentityRole.Liquidado, roles);
        Assert.Contains(IdentityRole.CarryForward, roles);
    }

    // ── Test 3 — snapshot exacto de todos los campos originales ─────────────────

    [Fact]
    public async Task RollbackAsync_ConservaElSnapshotExactoDeCadaLinkOriginal()
    {
        var dbName = nameof(RollbackAsync_ConservaElSnapshotExactoDeCadaLinkOriginal);
        var groupId = Guid.NewGuid();
        var originalCreatedAt = new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc);
        var links = BuildGroup(groupId, memberCount: 2, createdBy: "DedupeEngine", createdAtUtc: originalCreatedAt);
        await SeedAsync(dbName, links.ToArray());

        await using var db = OpenDb(dbName);
        await NewService(db).RollbackAsync(groupId, "facunav", "verificación de snapshot", default);

        var rollback = await db.MovementIdentityLinkRollbacks.SingleAsync(r => r.IdentityGroupId == groupId);
        var members = await db.MovementIdentityLinkRollbackMembers
            .Where(m => m.RollbackId == rollback.Id).ToListAsync();

        foreach (var original in links)
        {
            var snapshot = Assert.Single(members, m => m.SourceId == original.SourceId);
            Assert.Equal(original.SourceEntityType, snapshot.SourceEntityType);
            Assert.Equal(original.Role, snapshot.Role);
            Assert.Equal(original.Classification, snapshot.Classification);
            Assert.Equal(original.Evidence, snapshot.Evidence);
            Assert.Equal(original.CreatedAtUtc, snapshot.OriginalCreatedAtUtc);
            Assert.Equal(original.CreatedBy, snapshot.OriginalCreatedBy);
        }
    }

    // ── Test 4 — grupo inexistente ────────────────────────────────────────────

    [Fact]
    public async Task RollbackAsync_ConGrupoInexistente_DevuelveNotFound()
    {
        var dbName = nameof(RollbackAsync_ConGrupoInexistente_DevuelveNotFound);
        await using var db = OpenDb(dbName);

        var result = await NewService(db).RollbackAsync(Guid.NewGuid(), "facunav", "no debería encontrar nada", default);

        Assert.Equal(RollbackOutcome.NotFound, result.Outcome);
        Assert.Equal(0, result.MembersAffected);
        Assert.Empty(await db.MovementIdentityLinkRollbacks.ToListAsync());
    }

    // ── Test 5 — segundo rollback -> AlreadyRolledBack, sin datos adicionales ────

    [Fact]
    public async Task RollbackAsync_Repetido_DevuelveAlreadyRolledBackSinDuplicarAuditoria()
    {
        var dbName = nameof(RollbackAsync_Repetido_DevuelveAlreadyRolledBackSinDuplicarAuditoria);
        var groupId = Guid.NewGuid();
        await SeedAsync(dbName, BuildGroup(groupId).ToArray());

        RollbackResult first, second;
        await using (var db = OpenDb(dbName))
            first = await NewService(db).RollbackAsync(groupId, "facunav", "primer intento", default);
        await using (var db = OpenDb(dbName))
            second = await NewService(db).RollbackAsync(groupId, "facunav", "segundo intento", default);

        Assert.Equal(RollbackOutcome.RolledBack, first.Outcome);
        Assert.Equal(RollbackOutcome.AlreadyRolledBack, second.Outcome);
        Assert.Equal(0, second.MembersAffected);

        await using var verifyDb = OpenDb(dbName);
        // Un solo registro de auditoría -- el segundo intento no insertó nada nuevo.
        Assert.Single(await verifyDb.MovementIdentityLinkRollbacks.Where(r => r.IdentityGroupId == groupId).ToListAsync());
    }

    // ── Test 6 — reason vacío ─────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task RollbackAsync_ConReasonVacio_LanzaArgumentException(string? reason)
    {
        var dbName = nameof(RollbackAsync_ConReasonVacio_LanzaArgumentException) + Guid.NewGuid();
        await using var db = OpenDb(dbName);

        await Assert.ThrowsAsync<ArgumentException>(
            () => NewService(db).RollbackAsync(Guid.NewGuid(), "facunav", reason!, default));
    }

    // ── Test 7 — actor persistido correctamente ──────────────────────────────

    [Fact]
    public async Task RollbackAsync_PersisteElActorRecibido()
    {
        var dbName = nameof(RollbackAsync_PersisteElActorRecibido);
        var groupId = Guid.NewGuid();
        await SeedAsync(dbName, BuildGroup(groupId).ToArray());

        await using var db = OpenDb(dbName);
        await NewService(db).RollbackAsync(groupId, "ApiKeyUser", "prueba de actor", default);

        var rollback = await db.MovementIdentityLinkRollbacks.SingleAsync(r => r.IdentityGroupId == groupId);
        Assert.Equal("ApiKeyUser", rollback.RolledBackBy);
    }

    // ── Test 8 — nunca modifica BankStatements ───────────────────────────────

    [Fact]
    public async Task RollbackAsync_NuncaModificaBankStatements()
    {
        var dbName = nameof(RollbackAsync_NuncaModificaBankStatements);
        var groupId = Guid.NewGuid();
        var links = BuildGroup(groupId, memberCount: 2);
        var statement = new BankStatement
        {
            Id = links[0].SourceId,
            Date = new DateTime(2026, 8, 1),
            Amount = -100m,
            Concept = "TRANSFERENCIA",
            SourceFile = "archivo.xls",
            BankName = "BBVA",
            Currency = "ARS",
            ExternalId = "external-id-test",
            ImportedAtUtc = FixedNow,
        };

        await using (var seedDb = OpenDb(dbName))
        {
            seedDb.MovementIdentityLinks.AddRange(links);
            seedDb.BankStatements.Add(statement);
            await seedDb.SaveChangesAsync();
        }

        await using var db = OpenDb(dbName);
        await NewService(db).RollbackAsync(groupId, "facunav", "no debe tocar BankStatements", default);

        var statementAfter = await db.BankStatements.AsNoTracking().SingleAsync(s => s.Id == statement.Id);
        Assert.Equal(statement.Amount, statementAfter.Amount);
        Assert.Equal(statement.Concept, statementAfter.Concept);
        Assert.Equal(1, await db.BankStatements.CountAsync()); // no se borró ni se agregó ninguna
    }

    // ── Test 11 — aislamiento entre grupos ───────────────────────────────────

    [Fact]
    public async Task RollbackAsync_DeUnGrupo_NoAfectaOtroGrupo()
    {
        var dbName = nameof(RollbackAsync_DeUnGrupo_NoAfectaOtroGrupo);
        var groupA = Guid.NewGuid();
        var groupB = Guid.NewGuid();
        await SeedAsync(dbName, BuildGroup(groupA).Concat(BuildGroup(groupB)).ToArray());

        await using (var db = OpenDb(dbName))
            await NewService(db).RollbackAsync(groupA, "facunav", "revertir solo A", default);

        await using var verifyDb = OpenDb(dbName);
        Assert.Empty(await verifyDb.MovementIdentityLinks.Where(l => l.IdentityGroupId == groupA).ToListAsync());
        Assert.Equal(2, await verifyDb.MovementIdentityLinks.Where(l => l.IdentityGroupId == groupB).CountAsync());
        Assert.Empty(await verifyDb.MovementIdentityLinkRollbacks.Where(r => r.IdentityGroupId == groupB).ToListAsync());
    }

    // ── Concurrencia — backstop del índice único ─────────────────────────────
    //
    // LIMITACIÓN DOCUMENTADA (mismo criterio que ApplyAsync_Concurrente_NoDuplicaIdentidad
    // en DedupeEngineTests): el proveedor InMemory de EF Core no reproduce fielmente el
    // locking/unicidad real de Postgres. Este test prueba que, con dos DbContext
    // separados sobre el mismo nombre de base, el segundo intento de rollback del mismo
    // grupo no encuentra links para borrar (porque el primero ya los borró) y por lo
    // tanto no duplica auditoría -- no es una garantía de comportamiento real de
    // Postgres bajo una carrera genuina a nivel de fila, que depende del índice único
    // real (ver doc-comment de MovementIdentityLinkRollback y el catch de SQLSTATE 23505
    // en el servicio, no ejercitado por este test).

    [Fact]
    public async Task RollbackAsync_DosCorridasSecuencialesSobreElMismoGrupo_NoDuplicanAuditoria()
    {
        var dbName = nameof(RollbackAsync_DosCorridasSecuencialesSobreElMismoGrupo_NoDuplicanAuditoria);
        var groupId = Guid.NewGuid();
        await SeedAsync(dbName, BuildGroup(groupId).ToArray());

        await using var dbA = OpenDb(dbName);
        await using var dbB = OpenDb(dbName);

        var resultA = await NewService(dbA).RollbackAsync(groupId, "actorA", "corrida A", default);
        var resultB = await NewService(dbB).RollbackAsync(groupId, "actorB", "corrida B", default);

        Assert.Equal(RollbackOutcome.RolledBack, resultA.Outcome);
        Assert.Equal(RollbackOutcome.AlreadyRolledBack, resultB.Outcome);

        await using var verifyDb = OpenDb(dbName);
        Assert.Single(await verifyDb.MovementIdentityLinkRollbacks.Where(r => r.IdentityGroupId == groupId).ToListAsync());
    }
}
