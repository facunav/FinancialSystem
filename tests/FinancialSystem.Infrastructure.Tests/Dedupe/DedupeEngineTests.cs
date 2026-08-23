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
/// Cobertura de <see cref="DedupeEngine"/> -- traducción a C# de la especificación
/// DEDUPE-003-CONV, ya validada mecánicamente en SQL sobre dataset sintético
/// (DEDUPE-002.1-CONV/003-CONV) y con datos reales de `financialsystem` para la señal M
/// (ver DEDUPE-RECONCILIACION-IMPORT-vs-DEDUPE.md). Estos tests reproducen los mismos
/// casos ya demostrados, no inventan escenarios nuevos.
///
/// Mismo patrón de <c>AppDbContext</c> InMemory que ya usa InvestigationsHandlerTests/
/// ReviewEngineTests: cada paso abre su propio contexto sobre el mismo nombre de base.
/// </summary>
public class DedupeEngineTests
{
    private static readonly DateTime FixedNow = new(2026, 8, 23, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Account = Guid.NewGuid();

    private sealed class FakeDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => FixedNow;
    }

    private static AppDbContext OpenDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private static BankStatement Bs(
        DateTime date, decimal amount, string concept, string sourceFile, int rowNumber,
        decimal? balance = null, [System.Runtime.CompilerServices.CallerMemberName] string externalIdSeed = "")
    {
        var id = Guid.NewGuid();
        return new BankStatement
        {
            Id = id,
            Date = date,
            Amount = amount,
            Concept = concept,
            SourceFile = sourceFile,
            RowNumber = rowNumber,
            Balance = balance,
            BankName = "BBVA",
            Currency = "ARS",
            ExternalId = $"{externalIdSeed}-{sourceFile}-{rowNumber}-{Guid.NewGuid():N}",
            ImportedAtUtc = FixedNow,
            FinancialAccountId = Account,
        };
    }

    private static async Task SeedAsync(string dbName, params BankStatement[] statements)
    {
        await using var db = OpenDb(dbName);
        db.BankStatements.AddRange(statements);
        await db.SaveChangesAsync();
    }

    // ── Casos de clasificación (reproducen DEDUPE-002.1-CONV/003-CONV) ─────────────

    [Fact]
    public async Task NroToOp_ConSufijoCoincidente_EsFuerte()
    {
        var dbName = nameof(NroToOp_ConSufijoCoincidente_EsFuerte);
        var pendiente = Bs(new DateTime(2026, 8, 1), -33333.00m, "PAGO CON VISA DEBITO Nro:100333", "archivo1.xls", 1);
        var liquidado = Bs(new DateTime(2026, 8, 2), -33333.00m, "PAGO CON VISA DEBITO 96477108 OP0333", "archivo2.xls", 1);
        await SeedAsync(dbName, pendiente, liquidado);

        var results = await Preview(dbName);

        var result = Assert.Single(results);
        Assert.Equal(IdentityClassification.Fuerte, result.Classification);
        Assert.Equal(pendiente.Id, result.PendienteId);
        Assert.Equal(liquidado.Id, result.LiquidadoId);
    }

    [Fact]
    public async Task NroToOp_ConSufijoContradictorio_EsDescartadoYNoAparece()
    {
        var dbName = nameof(NroToOp_ConSufijoContradictorio_EsDescartadoYNoAparece);
        var pendiente = Bs(new DateTime(2026, 8, 1), -34000.00m, "PAGO CON VISA DEBITO Nro:100340", "archivo1.xls", 1);
        var liquidado = Bs(new DateTime(2026, 8, 2), -34000.00m, "PAGO CON VISA DEBITO 96477108 OP9999", "archivo2.xls", 1);
        await SeedAsync(dbName, pendiente, liquidado);

        var results = await Preview(dbName);

        // DESCARTADO nunca aparece en el resultado de Preview (no es un candidato a mostrar).
        Assert.Empty(results);
    }

    [Fact]
    public async Task NroToTransferencia_SinNumeroSobreviviente_EsPosible()
    {
        var dbName = nameof(NroToTransferencia_SinNumeroSobreviviente_EsPosible);
        var pendiente = Bs(new DateTime(2026, 8, 1), -44444.00m, "TRANSF DEBITO Nro:400444", "archivo1.xls", 1);
        var liquidado = Bs(new DateTime(2026, 8, 3), -44444.00m, "TRANSFERENCIA", "archivo2.xls", 1);
        await SeedAsync(dbName, pendiente, liquidado);

        var result = Assert.Single(await Preview(dbName));
        Assert.Equal(IdentityClassification.Posible, result.Classification);
    }

    [Fact]
    public async Task DosLiquidadosCandidatos_EsIndeterminado_ParaAmbos()
    {
        var dbName = nameof(DosLiquidadosCandidatos_EsIndeterminado_ParaAmbos);
        var pendiente = Bs(new DateTime(2026, 8, 1), -66666.00m, "TRANSF DEBITO Nro:600666", "archivo1.xls", 1);
        var liquidado1 = Bs(new DateTime(2026, 8, 3), -66666.00m, "TRANSFERENCIA", "archivo2.xls", 1);
        var liquidado2 = Bs(new DateTime(2026, 8, 4), -66666.00m, "TRANSFERENCIA", "archivo3.xls", 1);
        await SeedAsync(dbName, pendiente, liquidado1, liquidado2);

        var results = await Preview(dbName);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(IdentityClassification.Indeterminado, r.Classification));
        // Política conservadora: ninguno se resuelve a FUERTE ni se favorece a uno sobre otro.
        Assert.DoesNotContain(results, r => r.Classification == IdentityClassification.Fuerte);
    }

    [Fact]
    public async Task CarryForward_ColapsaACandidatoUnico_YFuerteIncluyeLosTresMiembros()
    {
        var dbName = nameof(CarryForward_ColapsaACandidatoUnico_YFuerteIncluyeLosTresMiembros);
        var pendiente = Bs(new DateTime(2026, 8, 1), -77777.00m, "TRANSF CREDITO Nro:700777", "archivo1.xls", 1);
        var liquidadoA = Bs(new DateTime(2026, 8, 3), -77777.00m, "TRANSFERENCIA", "archivo2.xls", 1, balance: 100000m);
        var liquidadoB = Bs(new DateTime(2026, 8, 3), -77777.00m, "TRANSFERENCIA", "archivo3.xls", 1, balance: 100000m);
        await SeedAsync(dbName, pendiente, liquidadoA, liquidadoB);

        var result = Assert.Single(await Preview(dbName));
        // Un solo candidato lógico pese a 2 representaciones físicas del liquidado.
        Assert.Equal(IdentityClassification.Posible, result.Classification); // sin número -> POSIBLE, no compite
        Assert.Single(result.CarryForwardMemberIds);
    }

    [Fact]
    public async Task ImporteRecurrente_BloqueaFuerte_GuardianK()
    {
        var dbName = nameof(ImporteRecurrente_BloqueaFuerte_GuardianK);
        // Cadena que confirma en ambos lados (igual que CASO G2 del SQL) + importe con
        // más de 1 aparición en la familia TRANSFERENCIA -> nunca debe llegar a FUERTE.
        var pendiente = Bs(new DateTime(2026, 8, 1), -10000.00m, "TRANSFERENCIA", "archivo1.xls", 1, balance: 200000m);
        var pendienteVecino = Bs(new DateTime(2026, 8, 1), -3000.00m, "PAGO CON VISA DEBITO OP5001", "archivo1.xls", 2, balance: 210000m);
        var liquidado = Bs(new DateTime(2026, 8, 2), -10000.00m, "TRANSFERENCIA", "archivo2.xls", 1, balance: 80000m);
        var liquidadoVecino = Bs(new DateTime(2026, 8, 2), -2000.00m, "PAGO CON VISA DEBITO OP5002", "archivo2.xls", 2, balance: 90000m);
        // Repeticiones que hacen que -10000 aparezca más de una vez como TRANSFERENCIA en la cuenta.
        var otraTransferencia = Bs(new DateTime(2026, 5, 5), -10000.00m, "TRANSFERENCIA", "archivo1.xls", 20);

        await SeedAsync(dbName, pendiente, pendienteVecino, liquidado, liquidadoVecino, otraTransferencia);

        var result = Assert.Single(await Preview(dbName));
        Assert.Equal(IdentityClassification.Posible, result.Classification);
        Assert.Contains("K", result.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SinImporteRecurrente_ConCadenaCompleta_EsFuerteSinNumero()
    {
        var dbName = nameof(SinImporteRecurrente_ConCadenaCompleta_EsFuerteSinNumero);
        // Mismo armado que el test anterior pero SIN la repetición -> debe alcanzar FUERTE.
        var pendiente = Bs(new DateTime(2026, 8, 1), -10500.00m, "DEBITO VARIOS PENDIENTE", "archivo1.xls", 1, balance: 200000m);
        var pendienteVecino = Bs(new DateTime(2026, 8, 1), -3000.00m, "PAGO CON VISA DEBITO OP5001", "archivo1.xls", 2, balance: 210500m);
        var liquidado = Bs(new DateTime(2026, 8, 2), -10500.00m, "DEBITO VARIOS LIQUIDADO", "archivo2.xls", 1, balance: 80000m);
        var liquidadoVecino = Bs(new DateTime(2026, 8, 2), -2000.00m, "PAGO CON VISA DEBITO OP5002", "archivo2.xls", 2, balance: 90500m);

        await SeedAsync(dbName, pendiente, pendienteVecino, liquidado, liquidadoVecino);

        var result = Assert.Single(await Preview(dbName));
        Assert.Equal(IdentityClassification.Fuerte, result.Classification);
    }

    [Fact]
    public async Task NroReutilizadoConImporteDistinto_GuardianM_BloqueaFuerte()
    {
        var dbName = nameof(NroReutilizadoConImporteDistinto_GuardianM_BloqueaFuerte);
        // Reproduce el hallazgo real de financialsystem: mismo Nro completo, importes
        // distintos en otra parte de la cuenta -> nunca puede sostener FUERTE vía D+E,
        // aunque el sufijo de ESTE par en particular coincida.
        var pendiente = Bs(new DateTime(2026, 8, 1), -55555.00m, "PAGO DE HABERES Nro:99999999", "archivo1.xls", 1);
        var liquidado = Bs(new DateTime(2026, 8, 2), -55555.00m, "PAGO DE HABERES 96477108 OP9999", "archivo2.xls", 1);
        var otroImporteMismoNro = Bs(new DateTime(2026, 6, 1), -99999.00m, "PAGO DE HABERES Nro:99999999", "archivo3.xls", 1);

        await SeedAsync(dbName, pendiente, liquidado, otroImporteMismoNro);

        var result = Assert.Single(await Preview(dbName));
        Assert.Equal(IdentityClassification.Posible, result.Classification);
        Assert.Contains("M:", result.Evidence);
    }

    [Fact]
    public async Task NroNoReutilizado_ConSufijoCoincidente_SigueSiendoFuerte()
    {
        var dbName = nameof(NroNoReutilizado_ConSufijoCoincidente_SigueSiendoFuerte);
        // Control: sin reutilización de Nro (caso 026888-equivalente), M no debe bloquear nada.
        var pendiente = Bs(new DateTime(2026, 7, 8), -1100000.00m, "TRANSF DEBITO Nro:026888", "archivo1.xls", 1);
        var liquidado = Bs(new DateTime(2026, 7, 9), -1100000.00m, "PAGO CON VISA DEBITO 1 OP6888", "archivo2.xls", 1);
        await SeedAsync(dbName, pendiente, liquidado);

        var result = Assert.Single(await Preview(dbName));
        Assert.Equal(IdentityClassification.Fuerte, result.Classification);
    }

    [Fact]
    public async Task FueraDeVentanaDeDescubrimiento_NoGeneraCandidato()
    {
        var dbName = nameof(FueraDeVentanaDeDescubrimiento_NoGeneraCandidato);
        var pendiente = Bs(new DateTime(2026, 8, 1), -12345.00m, "TRANSF DEBITO Nro:112233", "archivo1.xls", 1);
        // 15 días de diferencia -> fuera de la ventana de descubrimiento (±10).
        var fueraDeVentana = Bs(new DateTime(2026, 8, 16), -12345.00m, "TRANSFERENCIA", "archivo2.xls", 1);
        await SeedAsync(dbName, pendiente, fueraDeVentana);

        Assert.Empty(await Preview(dbName));
    }

    [Fact]
    public async Task DentroDeVentana_PeroFechaSolaNuncaEsSuficienteParaFuerte()
    {
        var dbName = nameof(DentroDeVentana_PeroFechaSolaNuncaEsSuficienteParaFuerte);
        // Dentro de la ventana, sin número, sin cadena, con importe recurrente -- la sola
        // cercanía de fecha no debe empujar esto a FUERTE.
        var pendiente = Bs(new DateTime(2026, 8, 1), -50000.00m, "TRANSFERENCIA", "archivo1.xls", 1);
        var liquidado = Bs(new DateTime(2026, 8, 3), -50000.00m, "TRANSFERENCIA", "archivo2.xls", 1);
        var recurrente = Bs(new DateTime(2026, 5, 1), -50000.00m, "TRANSFERENCIA", "archivo1.xls", 30);
        await SeedAsync(dbName, pendiente, liquidado, recurrente);

        var result = Assert.Single(await Preview(dbName));
        Assert.NotEqual(IdentityClassification.Fuerte, result.Classification);
    }

    [Fact]
    public async Task AusenciaDeCobertura_NoDegradaUnCandidatoYaEncontrado_COB6()
    {
        var dbName = nameof(AusenciaDeCobertura_NoDegradaUnCandidatoYaEncontrado_COB6);
        // Reproduce COB6 (DEDUPE-003-CONV, addendum de cobertura): un candidato único que
        // cumple TODAS las condiciones de FUERTE, más filas no relacionadas de otro
        // archivo cuya cobertura de fechas incluye la fecha del pendiente sin tener
        // ninguna fila de ese importe -- esa ausencia NO debe degradar el candidato que
        // SÍ se encontró en otro archivo.
        var pendiente = Bs(new DateTime(2026, 8, 1), -74000.00m, "DEBITO VARIOS PENDIENTE", "archivo_pendiente.xls", 1, balance: 250000m);
        var pendienteVecino = Bs(new DateTime(2026, 8, 1), -3300.00m, "PAGO CON VISA DEBITO OP6001", "archivo_pendiente.xls", 2, balance: 324000m);
        var liquidado = Bs(new DateTime(2026, 8, 2), -74000.00m, "DEBITO VARIOS LIQUIDADO", "archivo_real.xls", 1, balance: 95000m);
        var liquidadoVecino = Bs(new DateTime(2026, 8, 2), -2200.00m, "PAGO CON VISA DEBITO OP6002", "archivo_real.xls", 2, balance: 169000m);
        // Archivo "esperado" no relacionado, cubre 2026-07-29..2026-08-04 (incluye la
        // fecha del pendiente) pero SIN ninguna fila de -74000.
        var esperadoRuido1 = Bs(new DateTime(2026, 7, 29), -6000.00m, "PAGO CON VISA DEBITO OP6003", "archivo_esperado.xls", 1);
        var esperadoRuido2 = Bs(new DateTime(2026, 8, 4), -3000.00m, "PAGO CON VISA DEBITO OP6004", "archivo_esperado.xls", 2);

        await SeedAsync(dbName, pendiente, pendienteVecino, liquidado, liquidadoVecino, esperadoRuido1, esperadoRuido2);

        var result = Assert.Single(await Preview(dbName));
        Assert.Equal(IdentityClassification.Fuerte, result.Classification);
    }

    // ── No-destructividad, cardinalidad, idempotencia, Preview de solo lectura ─────

    [Fact]
    public async Task PreviewAsync_NuncaModificaNiEliminaBankStatements()
    {
        var dbName = nameof(PreviewAsync_NuncaModificaNiEliminaBankStatements);
        var pendiente = Bs(new DateTime(2026, 8, 1), -33333.00m, "PAGO CON VISA DEBITO Nro:100333", "archivo1.xls", 1);
        var liquidado = Bs(new DateTime(2026, 8, 2), -33333.00m, "PAGO CON VISA DEBITO 96477108 OP0333", "archivo2.xls", 1);
        await SeedAsync(dbName, pendiente, liquidado);

        await Preview(dbName);

        await using var db = OpenDb(dbName);
        var stillThere = await db.BankStatements.AsNoTracking().ToListAsync();
        Assert.Equal(2, stillThere.Count);
        Assert.Contains(stillThere, s => s.Id == pendiente.Id && s.Concept == pendiente.Concept);
        Assert.Contains(stillThere, s => s.Id == liquidado.Id && s.Concept == liquidado.Concept);
    }

    [Fact]
    public async Task PreviewAsync_NuncaPersisteNiCreaLinks()
    {
        var dbName = nameof(PreviewAsync_NuncaPersisteNiCreaLinks);
        var pendiente = Bs(new DateTime(2026, 8, 1), -33333.00m, "PAGO CON VISA DEBITO Nro:100333", "archivo1.xls", 1);
        var liquidado = Bs(new DateTime(2026, 8, 2), -33333.00m, "PAGO CON VISA DEBITO 96477108 OP0333", "archivo2.xls", 1);
        await SeedAsync(dbName, pendiente, liquidado);

        var results = await Preview(dbName);
        Assert.Equal(IdentityClassification.Fuerte, Assert.Single(results).Classification);

        await using var db = OpenDb(dbName);
        Assert.Empty(await db.MovementIdentityLinks.ToListAsync());
    }

    [Fact]
    public async Task ApplyAsync_PersisteSoloFuerte_NuncaPosibleNiIndeterminado()
    {
        var dbName = nameof(ApplyAsync_PersisteSoloFuerte_NuncaPosibleNiIndeterminado);
        var fuertePendiente = Bs(new DateTime(2026, 8, 1), -33333.00m, "PAGO CON VISA DEBITO Nro:100333", "archivo1.xls", 1);
        var fuerteLiquidado = Bs(new DateTime(2026, 8, 2), -33333.00m, "PAGO CON VISA DEBITO 96477108 OP0333", "archivo2.xls", 1);
        var posiblePendiente = Bs(new DateTime(2026, 8, 1), -44444.00m, "TRANSF DEBITO Nro:400444", "archivo1.xls", 3);
        var posibleLiquidado = Bs(new DateTime(2026, 8, 3), -44444.00m, "TRANSFERENCIA", "archivo2.xls", 3);
        await SeedAsync(dbName, fuertePendiente, fuerteLiquidado, posiblePendiente, posibleLiquidado);

        var results = await Preview(dbName);
        Assert.Equal(2, results.Count);

        await using (var db = OpenDb(dbName))
        {
            var engine = new DedupeEngine(db, new FakeDateTimeProvider());
            var created = await engine.ApplyAsync(results, "test");
            Assert.Equal(1, created); // solo el grupo FUERTE
        }

        await using var verifyDb = OpenDb(dbName);
        var links = await verifyDb.MovementIdentityLinks.ToListAsync();
        Assert.Equal(2, links.Count); // pendiente + liquidado del único grupo FUERTE
        Assert.All(links, l => Assert.Equal(IdentityClassification.Fuerte, l.Classification));
        Assert.Contains(links, l => l.SourceId == fuertePendiente.Id);
        Assert.Contains(links, l => l.SourceId == fuerteLiquidado.Id);
        Assert.DoesNotContain(links, l => l.SourceId == posiblePendiente.Id || l.SourceId == posibleLiquidado.Id);
    }

    [Fact]
    public async Task ApplyAsync_EsIdempotente_CorrerDosVecesNoDuplicaLinks()
    {
        var dbName = nameof(ApplyAsync_EsIdempotente_CorrerDosVecesNoDuplicaLinks);
        var pendiente = Bs(new DateTime(2026, 8, 1), -33333.00m, "PAGO CON VISA DEBITO Nro:100333", "archivo1.xls", 1);
        var liquidado = Bs(new DateTime(2026, 8, 2), -33333.00m, "PAGO CON VISA DEBITO 96477108 OP0333", "archivo2.xls", 1);
        await SeedAsync(dbName, pendiente, liquidado);

        var results = await Preview(dbName);

        int firstRun, secondRun;
        await using (var db = OpenDb(dbName))
        {
            var engine = new DedupeEngine(db, new FakeDateTimeProvider());
            firstRun = await engine.ApplyAsync(results, "test");
        }
        await using (var db = OpenDb(dbName))
        {
            var engine = new DedupeEngine(db, new FakeDateTimeProvider());
            secondRun = await engine.ApplyAsync(results, "test");
        }

        Assert.Equal(1, firstRun);
        Assert.Equal(0, secondRun); // segunda corrida no inserta nada nuevo

        await using var verifyDb = OpenDb(dbName);
        var links = await verifyDb.MovementIdentityLinks.ToListAsync();
        Assert.Equal(2, links.Count); // sigue habiendo exactamente 2 filas, no 4
    }

    [Fact]
    public async Task Cardinalidad_UnaRepresentacionFisica_NuncaTieneMasDeUnLink()
    {
        var dbName = nameof(Cardinalidad_UnaRepresentacionFisica_NuncaTieneMasDeUnLink);
        var pendiente = Bs(new DateTime(2026, 8, 1), -33333.00m, "PAGO CON VISA DEBITO Nro:100333", "archivo1.xls", 1);
        var liquidado = Bs(new DateTime(2026, 8, 2), -33333.00m, "PAGO CON VISA DEBITO 96477108 OP0333", "archivo2.xls", 1);
        await SeedAsync(dbName, pendiente, liquidado);

        var results = await Preview(dbName);
        await using (var db = OpenDb(dbName))
        {
            var engine = new DedupeEngine(db, new FakeDateTimeProvider());
            await engine.ApplyAsync(results, "test");
        }

        await using var verifyDb = OpenDb(dbName);
        var links = await verifyDb.MovementIdentityLinks.ToListAsync();
        var groupedBySource = links.GroupBy(l => (l.SourceEntityType, l.SourceId));
        Assert.All(groupedBySource, g => Assert.Single(g)); // nunca más de 1 fila por representación física
    }

    private static async Task<IReadOnlyList<DedupeCandidateResult>> Preview(string dbName)
    {
        await using var db = OpenDb(dbName);
        var engine = new DedupeEngine(db, new FakeDateTimeProvider());
        return await engine.PreviewAsync();
    }
}
