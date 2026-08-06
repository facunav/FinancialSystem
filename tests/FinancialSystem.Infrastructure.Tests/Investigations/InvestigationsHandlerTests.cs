using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Investigations.Commands;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Memory;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Investigations;

/// <summary>
/// Cobertura de los 4 handlers del módulo Investigations -- se verificó la
/// implementación real (src/FinancialSystem.Application/Investigations/Commands/):
/// no existen handlers adicionales más allá de estos 4 (sin queries, sin otros
/// commands). Cada región cubre uno de los handlers, pensada como red de seguridad
/// para futuras modificaciones del módulo (ver PATCH-039, Epic W).
///
/// Todos los handlers son <c>public sealed</c> -- no requieren InternalsVisibleTo.
///
/// Mismo patrón de <c>AppDbContext</c> InMemory que ya usa ClassifyMovementHandlerTests/
/// MovementLoaderTests/ReviewEngineTests/AuditReportServiceTests: cada paso lógico abre
/// su propio contexto sobre el mismo nombre de base, para no arrastrar entidades
/// trackeadas entre pasos. FixedNow (el reloj de FakeDateTimeProvider) y SeedTimestamp
/// (usado al sembrar datos de partida) son deliberadamente distintos, para que los
/// tests que verifican "esto no se tocó" puedan distinguir un valor realmente
/// intacto de uno que casualmente coincide con el reloj fijo.
///
/// Ningún test acá modifica los handlers ni corrige comportamiento -- ver PATCH-039.
/// </summary>
public class InvestigationsHandlerTests
{
    private static readonly DateTime FixedNow = new(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SeedTimestamp = new(2020, 3, 10, 0, 0, 0, DateTimeKind.Utc);

    private sealed class FakeDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => FixedNow;
    }

    private static AppDbContext OpenDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private static CreateInvestigationHandler CreateCreateHandler(string dbName) =>
        new(OpenDb(dbName), new FakeDateTimeProvider());

    private static LinkMovementToInvestigationHandler CreateLinkHandler(string dbName) =>
        new(OpenDb(dbName));

    private static AddInvestigationFindingHandler CreateFindingHandler(string dbName) =>
        new(OpenDb(dbName), new FakeDateTimeProvider());

    private static UpdateInvestigationStatusHandler CreateStatusHandler(string dbName) =>
        new(OpenDb(dbName), new FakeDateTimeProvider());

    private static async Task<Guid> SeedInvestigationAsync(
        string dbName,
        InvestigationStatus status = InvestigationStatus.Open,
        string? conclusion = null,
        string question = "Pregunta de prueba",
        DateTime? timestamp = null)
    {
        var at = timestamp ?? SeedTimestamp;
        await using var db = OpenDb(dbName);
        var investigation = new Investigation
        {
            Question = question,
            Status = status,
            Conclusion = conclusion,
            CreatedAt = at,
            UpdatedAt = at,
        };
        db.Investigations.Add(investigation);
        await db.SaveChangesAsync();
        return investigation.Id;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CreateInvestigationHandler
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Create_DatosCompletos_CreaInvestigationConEstadoOpenYConclusionNula()
    {
        var dbName = Guid.NewGuid().ToString();

        var result = await CreateCreateHandler(dbName).Handle(
            new CreateInvestigationCommand("¿Por qué se duplicó el pago?", "tarjeta-visa,duplicado"));

        Assert.Equal("¿Por qué se duplicó el pago?", result.Question);
        Assert.Equal("tarjeta-visa,duplicado", result.Tags);
        Assert.Equal(InvestigationStatus.Open, result.Status);
        Assert.Null(result.Conclusion);
    }

    [Fact]
    public async Task Create_DatosMinimos_TagsNull_SeAceptaSinFallar()
    {
        var dbName = Guid.NewGuid().ToString();

        var result = await CreateCreateHandler(dbName).Handle(
            new CreateInvestigationCommand("Pregunta minima", null));

        Assert.Equal("Pregunta minima", result.Question);
        Assert.Null(result.Tags);
        Assert.Equal(InvestigationStatus.Open, result.Status);
    }

    [Fact]
    public async Task Create_QuestionVacia_SeAceptaSinValidar()
    {
        // Comportamiento actual documentado tal cual: el handler no valida Question --
        // ni longitud minima ni contenido no vacio. Se persiste tal como llega.
        var dbName = Guid.NewGuid().ToString();

        var result = await CreateCreateHandler(dbName).Handle(
            new CreateInvestigationCommand(string.Empty, null));

        Assert.Equal(string.Empty, result.Question);

        await using var db = OpenDb(dbName);
        Assert.Equal(string.Empty, (await db.Investigations.SingleAsync()).Question);
    }

    [Fact]
    public async Task Create_AsignaCreatedAtYUpdatedAtDesdeElDateTimeProvider()
    {
        var dbName = Guid.NewGuid().ToString();

        var result = await CreateCreateHandler(dbName).Handle(
            new CreateInvestigationCommand("Pregunta", null));

        Assert.Equal(FixedNow, result.CreatedAt);
        Assert.Equal(FixedNow, result.UpdatedAt);
    }

    [Fact]
    public async Task Create_PersisteLaInvestigationEnLaBase()
    {
        var dbName = Guid.NewGuid().ToString();

        var result = await CreateCreateHandler(dbName).Handle(
            new CreateInvestigationCommand("Pregunta", "etiqueta"));

        await using var db = OpenDb(dbName);
        var persisted = await db.Investigations.SingleAsync();
        Assert.Equal(result.Id, persisted.Id);
        Assert.Equal("Pregunta", persisted.Question);
        Assert.Equal("etiqueta", persisted.Tags);
    }

    [Fact]
    public async Task Create_MultiplesLlamadas_CreanInvestigationsIndependientesConIdsDistintos()
    {
        var dbName = Guid.NewGuid().ToString();

        var first = await CreateCreateHandler(dbName).Handle(new CreateInvestigationCommand("Pregunta 1", null));
        var second = await CreateCreateHandler(dbName).Handle(new CreateInvestigationCommand("Pregunta 2", null));

        Assert.NotEqual(first.Id, second.Id);

        await using var db = OpenDb(dbName);
        Assert.Equal(2, await db.Investigations.CountAsync());
    }

    [Fact]
    public async Task Create_InvestigacionRecienCreada_NoTieneReferencesNiFindings()
    {
        var dbName = Guid.NewGuid().ToString();

        var result = await CreateCreateHandler(dbName).Handle(new CreateInvestigationCommand("Pregunta", null));

        await using var db = OpenDb(dbName);
        Assert.Empty(await db.InvestigationReferences.Where(r => r.InvestigationId == result.Id).ToListAsync());
        Assert.Empty(await db.InvestigationFindings.Where(f => f.InvestigationId == result.Id).ToListAsync());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // LinkMovementToInvestigationHandler
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Link_InvestigacionExistente_AsociaElMovimientoCorrectamente()
    {
        var dbName = Guid.NewGuid().ToString();
        var investigationId = await SeedInvestigationAsync(dbName);
        var sourceId = Guid.NewGuid();

        var result = await CreateLinkHandler(dbName).Handle(
            new LinkMovementToInvestigationCommand(investigationId, SourceEntityType.Transaction, sourceId));

        Assert.True(result.IsSuccess);
        Assert.Equal(LinkMovementToInvestigationOutcome.Linked, result.Outcome);

        await using var db = OpenDb(dbName);
        var reference = await db.InvestigationReferences.SingleAsync();
        Assert.Equal(investigationId, reference.InvestigationId);
        Assert.Equal(SourceEntityType.Transaction, reference.SourceEntityType);
        Assert.Equal(sourceId, reference.SourceId);
    }

    [Fact]
    public async Task Link_InvestigacionInexistente_DevuelveInvestigationNotFound()
    {
        var dbName = Guid.NewGuid().ToString();

        var result = await CreateLinkHandler(dbName).Handle(
            new LinkMovementToInvestigationCommand(Guid.NewGuid(), SourceEntityType.Transaction, Guid.NewGuid()));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Outcome);

        await using var db = OpenDb(dbName);
        Assert.Empty(await db.InvestigationReferences.ToListAsync());
    }

    [Fact]
    public async Task Link_MovimientoInexistente_IgualmenteAsociaLaReferencia()
    {
        // Comportamiento actual documentado tal cual, no corregido: el handler solo
        // verifica que la Investigation exista -- nunca consulta Transactions/
        // BankStatements para confirmar que el SourceId referenciado corresponda a un
        // movimiento real. Un SourceId arbitrario que no existe en ninguna tabla de
        // origen igual se asocia sin error.
        var dbName = Guid.NewGuid().ToString();
        var investigationId = await SeedInvestigationAsync(dbName);

        var result = await CreateLinkHandler(dbName).Handle(
            new LinkMovementToInvestigationCommand(investigationId, SourceEntityType.BankStatement, Guid.NewGuid()));

        Assert.True(result.IsSuccess);
        Assert.Equal(LinkMovementToInvestigationOutcome.Linked, result.Outcome);
    }

    [Fact]
    public async Task Link_AsociacionDuplicada_DevuelveAlreadyLinkedSinCrearOtraReferencia()
    {
        var dbName = Guid.NewGuid().ToString();
        var investigationId = await SeedInvestigationAsync(dbName);
        var command = new LinkMovementToInvestigationCommand(investigationId, SourceEntityType.Transaction, Guid.NewGuid());

        await CreateLinkHandler(dbName).Handle(command);
        var second = await CreateLinkHandler(dbName).Handle(command);

        Assert.True(second.IsSuccess);
        Assert.Equal(LinkMovementToInvestigationOutcome.AlreadyLinked, second.Outcome);

        await using var db = OpenDb(dbName);
        Assert.Equal(1, await db.InvestigationReferences.CountAsync());
    }

    [Fact]
    public async Task Link_MultiplesMovimientosParaLaMismaInvestigacion_CreaUnaReferenciaPorCada()
    {
        var dbName = Guid.NewGuid().ToString();
        var investigationId = await SeedInvestigationAsync(dbName);

        await CreateLinkHandler(dbName).Handle(
            new LinkMovementToInvestigationCommand(investigationId, SourceEntityType.Transaction, Guid.NewGuid()));
        await CreateLinkHandler(dbName).Handle(
            new LinkMovementToInvestigationCommand(investigationId, SourceEntityType.BankStatement, Guid.NewGuid()));

        await using var db = OpenDb(dbName);
        Assert.Equal(2, await db.InvestigationReferences.CountAsync(r => r.InvestigationId == investigationId));
    }

    [Fact]
    public async Task Link_NoModificaStatusNiUpdatedAtDeLaInvestigacion()
    {
        var dbName = Guid.NewGuid().ToString();
        var investigationId = await SeedInvestigationAsync(dbName, status: InvestigationStatus.Open);

        await CreateLinkHandler(dbName).Handle(
            new LinkMovementToInvestigationCommand(investigationId, SourceEntityType.Transaction, Guid.NewGuid()));

        await using var db = OpenDb(dbName);
        var investigation = await db.Investigations.SingleAsync();
        Assert.Equal(InvestigationStatus.Open, investigation.Status);
        Assert.Equal(SeedTimestamp, investigation.UpdatedAt);
    }

    [Fact]
    public async Task Link_MismoMovimientoEnDosInvestigacionesDistintas_CreaReferenciasIndependientes()
    {
        var dbName = Guid.NewGuid().ToString();
        var investigation1 = await SeedInvestigationAsync(dbName, question: "Investigacion 1");
        var investigation2 = await SeedInvestigationAsync(dbName, question: "Investigacion 2");
        var sourceId = Guid.NewGuid();

        await CreateLinkHandler(dbName).Handle(
            new LinkMovementToInvestigationCommand(investigation1, SourceEntityType.Transaction, sourceId));
        var result2 = await CreateLinkHandler(dbName).Handle(
            new LinkMovementToInvestigationCommand(investigation2, SourceEntityType.Transaction, sourceId));

        // No es AlreadyLinked: el chequeo de duplicado es por InvestigationId +
        // SourceEntityType + SourceId juntos, no por SourceId solo.
        Assert.Equal(LinkMovementToInvestigationOutcome.Linked, result2.Outcome);

        await using var db = OpenDb(dbName);
        Assert.Equal(2, await db.InvestigationReferences.CountAsync());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AddInvestigationFindingHandler
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AddFinding_InvestigacionExistente_AgregaElHallazgoCorrectamente()
    {
        var dbName = Guid.NewGuid().ToString();
        var investigationId = await SeedInvestigationAsync(dbName);

        var result = await CreateFindingHandler(dbName).Handle(
            new AddInvestigationFindingCommand(investigationId, "El pago se duplicó por un reintento del banco."));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.FindingId);
        Assert.Equal(FixedNow, result.CreatedAt);

        await using var db = OpenDb(dbName);
        var finding = await db.InvestigationFindings.SingleAsync();
        Assert.Equal(investigationId, finding.InvestigationId);
        Assert.Equal("El pago se duplicó por un reintento del banco.", finding.Text);
        Assert.Equal(FixedNow, finding.CreatedAt);
        Assert.Equal(result.FindingId, finding.Id);
    }

    [Fact]
    public async Task AddFinding_InvestigacionInexistente_DevuelveInvestigationNotFound()
    {
        var dbName = Guid.NewGuid().ToString();

        var result = await CreateFindingHandler(dbName).Handle(
            new AddInvestigationFindingCommand(Guid.NewGuid(), "Texto"));

        Assert.False(result.IsSuccess);
        Assert.Null(result.FindingId);
        Assert.Null(result.CreatedAt);
    }

    [Fact]
    public async Task AddFinding_MultiplesHallazgos_SeAgreganTodosIndependientemente()
    {
        var dbName = Guid.NewGuid().ToString();
        var investigationId = await SeedInvestigationAsync(dbName);

        await CreateFindingHandler(dbName).Handle(new AddInvestigationFindingCommand(investigationId, "Primero"));
        await CreateFindingHandler(dbName).Handle(new AddInvestigationFindingCommand(investigationId, "Segundo"));

        await using var db = OpenDb(dbName);
        var findings = await db.InvestigationFindings.Where(f => f.InvestigationId == investigationId).ToListAsync();
        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.Text == "Primero");
        Assert.Contains(findings, f => f.Text == "Segundo");
    }

    [Fact]
    public async Task AddFinding_TextoVacio_SeAceptaSinValidar()
    {
        var dbName = Guid.NewGuid().ToString();
        var investigationId = await SeedInvestigationAsync(dbName);

        var result = await CreateFindingHandler(dbName).Handle(
            new AddInvestigationFindingCommand(investigationId, string.Empty));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        Assert.Equal(string.Empty, (await db.InvestigationFindings.SingleAsync()).Text);
    }

    [Fact]
    public async Task AddFinding_NoModificaUpdatedAtDeLaInvestigacion()
    {
        var dbName = Guid.NewGuid().ToString();
        var investigationId = await SeedInvestigationAsync(dbName);

        await CreateFindingHandler(dbName).Handle(new AddInvestigationFindingCommand(investigationId, "Hallazgo"));

        await using var db = OpenDb(dbName);
        var investigation = await db.Investigations.SingleAsync();
        Assert.Equal(SeedTimestamp, investigation.UpdatedAt);
    }

    [Fact]
    public async Task AddFinding_HallazgoAgregado_NoApareceEnOtraInvestigacion()
    {
        var dbName = Guid.NewGuid().ToString();
        var investigation1 = await SeedInvestigationAsync(dbName, question: "Investigacion 1");
        var investigation2 = await SeedInvestigationAsync(dbName, question: "Investigacion 2");

        await CreateFindingHandler(dbName).Handle(new AddInvestigationFindingCommand(investigation1, "Hallazgo de la 1"));

        await using var db = OpenDb(dbName);
        Assert.Empty(await db.InvestigationFindings.Where(f => f.InvestigationId == investigation2).ToListAsync());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // UpdateInvestigationStatusHandler
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateStatus_TransicionValidaOpenAInProgress_CambiaElStatus()
    {
        var dbName = Guid.NewGuid().ToString();
        var investigationId = await SeedInvestigationAsync(dbName, status: InvestigationStatus.Open);

        var result = await CreateStatusHandler(dbName).Handle(
            new UpdateInvestigationStatusCommand(investigationId, InvestigationStatus.InProgress, null));

        Assert.True(result.IsSuccess);
        Assert.Equal(FixedNow, result.UpdatedAt);

        await using var db = OpenDb(dbName);
        var investigation = await db.Investigations.SingleAsync();
        Assert.Equal(InvestigationStatus.InProgress, investigation.Status);
        Assert.Equal(FixedNow, investigation.UpdatedAt);
    }

    [Fact]
    public async Task UpdateStatus_InvestigacionInexistente_DevuelveInvestigationNotFound()
    {
        var dbName = Guid.NewGuid().ToString();

        var result = await CreateStatusHandler(dbName).Handle(
            new UpdateInvestigationStatusCommand(Guid.NewGuid(), InvestigationStatus.InProgress, null));

        Assert.False(result.IsSuccess);
        Assert.Equal(UpdateInvestigationStatusFailureReason.InvestigationNotFound, result.FailureReason);
        Assert.Null(result.UpdatedAt);
    }

    [Fact]
    public async Task UpdateStatus_AResolvedConConclusion_GuardaLaConclusionYCambiaElStatus()
    {
        var dbName = Guid.NewGuid().ToString();
        var investigationId = await SeedInvestigationAsync(dbName, status: InvestigationStatus.InProgress);

        var result = await CreateStatusHandler(dbName).Handle(
            new UpdateInvestigationStatusCommand(investigationId, InvestigationStatus.Resolved, "Fue un reintento del banco."));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        var investigation = await db.Investigations.SingleAsync();
        Assert.Equal(InvestigationStatus.Resolved, investigation.Status);
        Assert.Equal("Fue un reintento del banco.", investigation.Conclusion);
    }

    [Fact]
    public async Task UpdateStatus_AResolvedSinConclusion_DevuelveConclusionRequiredYNoCambiaNada()
    {
        var dbName = Guid.NewGuid().ToString();
        var investigationId = await SeedInvestigationAsync(dbName, status: InvestigationStatus.Open);

        var result = await CreateStatusHandler(dbName).Handle(
            new UpdateInvestigationStatusCommand(investigationId, InvestigationStatus.Resolved, null));

        Assert.False(result.IsSuccess);
        Assert.Equal(UpdateInvestigationStatusFailureReason.ConclusionRequired, result.FailureReason);
        Assert.Null(result.UpdatedAt);

        await using var db = OpenDb(dbName);
        var investigation = await db.Investigations.SingleAsync();
        Assert.Equal(InvestigationStatus.Open, investigation.Status);
        Assert.Equal(SeedTimestamp, investigation.UpdatedAt);
    }

    [Fact]
    public async Task UpdateStatus_AResolvedConConclusionEnBlanco_DevuelveConclusionRequired()
    {
        // string.IsNullOrWhiteSpace: una Conclusion de solo espacios se trata igual que
        // vacía o nula -- no alcanza para satisfacer el requisito.
        var dbName = Guid.NewGuid().ToString();
        var investigationId = await SeedInvestigationAsync(dbName, status: InvestigationStatus.Open);

        var result = await CreateStatusHandler(dbName).Handle(
            new UpdateInvestigationStatusCommand(investigationId, InvestigationStatus.Resolved, "   "));

        Assert.False(result.IsSuccess);
        Assert.Equal(UpdateInvestigationStatusFailureReason.ConclusionRequired, result.FailureReason);
    }

    [Fact]
    public async Task UpdateStatus_ANoResolved_IgnoraLaConclusionRecibidaAunqueVengaConValor()
    {
        // Comportamiento actual documentado tal cual: si el Status destino no es
        // Resolved, la Conclusion recibida en el comando se ignora por completo -- no
        // se guarda, aunque venga con contenido no vacío.
        var dbName = Guid.NewGuid().ToString();
        var investigationId = await SeedInvestigationAsync(dbName, status: InvestigationStatus.Open, conclusion: null);

        var result = await CreateStatusHandler(dbName).Handle(
            new UpdateInvestigationStatusCommand(investigationId, InvestigationStatus.InProgress, "Esta conclusion se ignora"));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        var investigation = await db.Investigations.SingleAsync();
        Assert.Equal(InvestigationStatus.InProgress, investigation.Status);
        Assert.Null(investigation.Conclusion);
    }

    [Fact]
    public async Task UpdateStatus_DeResolvedAOtroEstado_NoLimpiaLaConclusionAnterior()
    {
        // Comportamiento actual documentado tal cual, no corregido: al salir de
        // Resolved hacia otro estado, la Conclusion previamente guardada no se borra --
        // el handler solo escribe Conclusion cuando el Status destino es Resolved, en
        // cualquier otro caso la deja como estaba.
        var dbName = Guid.NewGuid().ToString();
        var investigationId = await SeedInvestigationAsync(
            dbName, status: InvestigationStatus.Resolved, conclusion: "Conclusion original");

        var result = await CreateStatusHandler(dbName).Handle(
            new UpdateInvestigationStatusCommand(investigationId, InvestigationStatus.Open, null));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        var investigation = await db.Investigations.SingleAsync();
        Assert.Equal(InvestigationStatus.Open, investigation.Status);
        Assert.Equal("Conclusion original", investigation.Conclusion);
    }

    [Fact]
    public async Task UpdateStatus_CambioAlMismoEstado_NoFallaYActualizaUpdatedAt()
    {
        var dbName = Guid.NewGuid().ToString();
        var investigationId = await SeedInvestigationAsync(dbName, status: InvestigationStatus.Open);

        var result = await CreateStatusHandler(dbName).Handle(
            new UpdateInvestigationStatusCommand(investigationId, InvestigationStatus.Open, null));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        var investigation = await db.Investigations.SingleAsync();
        Assert.Equal(InvestigationStatus.Open, investigation.Status);
        Assert.Equal(FixedNow, investigation.UpdatedAt);
    }

    [Fact]
    public async Task UpdateStatus_CualquierTransicionEsAceptada_NoHayValidacionDeMaquinaDeEstados()
    {
        // Comportamiento actual documentado tal cual, no corregido: el handler no
        // valida transiciones de estado -- cualquier InvestigationStatus destino se
        // acepta sin importar el estado actual. No existe el concepto de "transición
        // inválida" en el código (ej. Discarded -> Open, que en una máquina de estados
        // estricta podría estar prohibido, funciona igual que cualquier otra).
        var dbName = Guid.NewGuid().ToString();
        var investigationId = await SeedInvestigationAsync(dbName, status: InvestigationStatus.Discarded);

        var result = await CreateStatusHandler(dbName).Handle(
            new UpdateInvestigationStatusCommand(investigationId, InvestigationStatus.Open, null));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        Assert.Equal(InvestigationStatus.Open, (await db.Investigations.SingleAsync()).Status);
    }

    [Fact]
    public async Task UpdateStatus_NoModificaReferencesNiFindingsExistentes()
    {
        var dbName = Guid.NewGuid().ToString();
        var investigationId = await SeedInvestigationAsync(dbName, status: InvestigationStatus.Open);
        await CreateLinkHandler(dbName).Handle(
            new LinkMovementToInvestigationCommand(investigationId, SourceEntityType.Transaction, Guid.NewGuid()));
        await CreateFindingHandler(dbName).Handle(new AddInvestigationFindingCommand(investigationId, "Hallazgo previo"));

        await CreateStatusHandler(dbName).Handle(
            new UpdateInvestigationStatusCommand(investigationId, InvestigationStatus.Resolved, "Conclusion"));

        await using var db = OpenDb(dbName);
        Assert.Equal(1, await db.InvestigationReferences.CountAsync(r => r.InvestigationId == investigationId));
        Assert.Equal(1, await db.InvestigationFindings.CountAsync(f => f.InvestigationId == investigationId));
    }
}
