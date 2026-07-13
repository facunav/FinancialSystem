using FinancialSystem.Application.Imports;
using FinancialSystem.Application.Parsing.Bbva;
using FinancialSystem.Application.Parsing.Bbva.Mastercard;
using FinancialSystem.Application.Parsing.Bbva.Visa;
using FinancialSystem.Application.Parsing.Mastercard;
using FinancialSystem.Infrastructure.Imports;
using FinancialSystem.Infrastructure.Imports.BankStatements;
using FinancialSystem.Infrastructure.Imports.Parsers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Imports;

/// <summary>
/// Cubre el mecanismo de selección de handlers/parsers auditado tras el fix de
/// "Detalle_mov_cuenta*.xls" (BbvaBankStatementFilePatterns no reconocía el nombre real
/// que exporta BBVA — ver docs/patch y el informe de auditoría posterior).
///
/// Valida el CONTRATO de selección — qué handler o parser gana para un archivo dado —
/// sin ejecutar HandleAsync/ParseAsync completo (evita tocar base de datos). No se
/// instancia FileImportRouter en sí (depende de IServiceScopeFactory/IDateTimeProvider
/// y persiste ImportBatch); en cambio, SelectHandler reproduce su única regla real
/// ("primer CanHandle=true gana") sobre handlers en el mismo orden en que
/// DependencyInjection.AddInfrastructure los registra hoy. Si ese orden o los patrones
/// de FileIngestionOptions cambian de forma que rompa un caso real ya soportado, estos
/// tests deberían fallar.
/// </summary>
public class ImportHandlerSelectionTests
{
    // ── Construcción de handlers reales con las dependencias mínimas que CanHandle
    //    necesita. IServiceScopeFactory/IImportFileSink no se usan en CanHandle (solo
    //    en HandleAsync, no ejercitado acá) — se pasan null! deliberadamente.

    private static BbvaBankStatementImportHandler CreateBankStatementHandler(FileIngestionOptions options)
    {
        var reader = new XlsBankStatementReader(NullLogger<XlsBankStatementReader>.Instance);
        var parser = new BbvaBankStatementParser(NullLogger<BbvaBankStatementParser>.Instance);
        var importer = new BbvaBankStatementImporter(
            reader, parser, null!, NullLogger<BbvaBankStatementImporter>.Instance);
        return new BbvaBankStatementImportHandler(
            importer, Options.Create(options), NullLogger<BbvaBankStatementImportHandler>.Instance);
    }

    private static BbvaDebitCardEnrichmentHandler CreateDebitCardHandler(FileIngestionOptions options)
    {
        var reader = new XlsBankStatementReader(NullLogger<XlsBankStatementReader>.Instance);
        var parser = new BbvaDebitCardParser(NullLogger<BbvaDebitCardParser>.Instance);
        return new BbvaDebitCardEnrichmentHandler(
            reader, parser, null!, Options.Create(options),
            NullLogger<BbvaDebitCardEnrichmentHandler>.Instance);
    }

    private static TransactionImportHandler CreateTransactionHandler()
        => new(null!, NullLogger<TransactionImportHandler>.Instance);

    private static IReadOnlyList<IFileImportHandler> ProductionOrderHandlers(FileIngestionOptions options) =>
    [
        CreateBankStatementHandler(options),
        CreateDebitCardHandler(options),
        CreateTransactionHandler(),
    ];

    /// <summary>Mismo algoritmo que FileImportRouter.RouteAsync: primer CanHandle=true gana.</summary>
    private static IFileImportHandler? SelectHandler(
        IReadOnlyList<IFileImportHandler> orderedHandlers, string fileName)
        => orderedHandlers.FirstOrDefault(h => h.CanHandle(fileName));

    // ── Escenario 1: Caja de Ahorro BBVA (nombre real) ─────────────────────────

    [Fact]
    public void RealBbvaBankStatementFileName_SelectsBbvaBankStatementImportHandler()
    {
        var handlers = ProductionOrderHandlers(new FileIngestionOptions());

        var selected = SelectHandler(handlers, "Detalle_mov_cuenta_12_07_2026.xls");

        Assert.IsType<BbvaBankStatementImportHandler>(selected);
    }

    // ── Escenario 2: Tarjeta de Débito BBVA (nombre real) ──────────────────────

    [Fact]
    public void RealBbvaDebitCardFileName_SelectsBbvaDebitCardEnrichmentHandler()
    {
        var handlers = ProductionOrderHandlers(new FileIngestionOptions());

        var selected = SelectHandler(handlers, "Últimos_movimientos_2026_07_12.xlsx");

        Assert.IsType<BbvaDebitCardEnrichmentHandler>(selected);
    }

    // ── Escenario 3: CSV genérico → catch-all ──────────────────────────────────

    [Fact]
    public void GenericCsvFile_FallsThroughToTransactionImportHandler()
    {
        var handlers = ProductionOrderHandlers(new FileIngestionOptions());

        var selected = SelectHandler(handlers, "resumen_cualquiera.csv");

        Assert.IsType<TransactionImportHandler>(selected);
    }

    // ── Contrato: TransactionImportHandler es siempre el último recurso ───────

    [Fact]
    public void TransactionImportHandler_IsRegisteredLastAndAcceptsEveryWatchedExtensionUnconditionally()
    {
        var handlers = ProductionOrderHandlers(new FileIngestionOptions());

        Assert.IsType<TransactionImportHandler>(handlers[^1]);

        // Es, a propósito, un catch-all sin filtro de nombre — por eso DEBE ir último:
        // si se registrara antes, capturaría cualquier archivo antes que los handlers
        // específicos tuvieran la oportunidad de reconocerlo.
        var catchAll = handlers[^1];
        foreach (var ext in FileIngestionOptions.WatchedExtensions)
            Assert.True(catchAll.CanHandle($"cualquier_archivo{ext}"),
                $"TransactionImportHandler debería aceptar la extensión {ext}");
    }

    // ── Punto 4 de la auditoría: patrones de distintos handlers no se superponen ──

    [Theory]
    [InlineData("Caja_Julio.xls")]
    [InlineData("movimientos_ahorros_junio.xls")]
    [InlineData("cuenta_corriente.xls")]
    [InlineData("Detalle_mov_cuenta_01_01_2026.xls")]
    public void RealBbvaBankStatementNamePatterns_CanNeverBeClaimedByTheDebitCardHandler(string fileName)
    {
        // BbvaDebitCardEnrichmentHandler exige .xlsx — un .xls nunca puede matchearlo,
        // sin importar el nombre. Garantía estructural, no depende del contenido de los
        // patrones de ninguno de los dos handlers.
        var debitCardHandler = CreateDebitCardHandler(new FileIngestionOptions());

        Assert.False(debitCardHandler.CanHandle(fileName));
    }

    // ── Escenarios 4 y 5: routing de PDF por contenido (FileParserFactory) ─────

    private static FileParserFactory CreateFactoryWithPdfParsers(IReadOnlyList<string> pdfLines)
    {
        var extractor = new FakePdfTextExtractor(pdfLines);
        // Mismo orden de registro que DependencyInjection.AddInfrastructure: Visa antes
        // que Mastercard — el orden es parte de lo que este archivo verifica.
        IFileParser[] parsers =
        [
            new BbvaVisaStatementParser(
                new BbvaTransactionLineParser(), extractor, NullLogger<BbvaVisaStatementParser>.Instance),
            new BbvaMastercardStatementParser(
                new MastercardTransactionLineParser(), extractor, NullLogger<BbvaMastercardStatementParser>.Instance),
        ];
        return new FileParserFactory(parsers, extractor, NullLogger<FileParserFactory>.Instance);
    }

    [Fact]
    public void VisaPdfContent_ResolvesToBbvaVisaParser()
    {
        var lines = new[]
        {
            "Banco BBVA Argentina",
            "Resumen de tu tarjeta VISA",
            "DETALLE DE CONSUMOS",
        };
        var factory = CreateFactoryWithPdfParsers(lines);

        var found = factory.TryGetParser("resumen_visa.pdf", out var parser);

        Assert.True(found);
        Assert.IsType<BbvaVisaStatementParser>(parser);
    }

    [Fact]
    public void MastercardOnlyPdfContent_ResolvesToBbvaMastercardParser()
    {
        // Sin la palabra "BBVA" en el contenido: caso inequívoco, sin superposición con
        // el fingerprint de Visa (ver test de la ambigüedad conocida, abajo).
        var lines = new[]
        {
            "Resumen de tu tarjeta MASTERCARD",
            "CONSUMOS DEL PERIODO",
        };
        var factory = CreateFactoryWithPdfParsers(lines);

        var found = factory.TryGetParser("resumen_mastercard.pdf", out var parser);

        Assert.True(found);
        Assert.IsType<BbvaMastercardStatementParser>(parser);
    }

    [Fact]
    public void KnownLimitation_PdfContainingBothBbvaAndMastercardText_CurrentlyResolvesToVisaParser()
    {
        // Documenta (no corrige) la ambigüedad ya reconocida explícitamente en el propio
        // comentario de BbvaMastercardStatementParser: "Si el extracto tiene 'BBVA' Y
        // 'MASTERCARD'... el orden de registro en DI determina qué parser gana. Por
        // diseño, BBVA Visa se registra primero". Este test no valida que esto sea lo
        // deseado — fija el comportamiento actual para que cambiarlo sea una decisión
        // consciente (nuevo fingerprint, reordenar DI), no un efecto secundario
        // silencioso de otro cambio.
        var lines = new[]
        {
            "Banco BBVA Argentina",
            "Resumen de tu tarjeta MASTERCARD",
            "CONSUMOS DEL PERIODO",
        };
        var factory = CreateFactoryWithPdfParsers(lines);

        factory.TryGetParser("resumen_bbva_mastercard.pdf", out var parser);

        Assert.IsType<BbvaVisaStatementParser>(parser);
    }

    private sealed class FakePdfTextExtractor(IReadOnlyList<string> lines) : IPdfTextExtractor
    {
        public Task<IReadOnlyList<string>> ExtractLinesAsync(string filePath, CancellationToken ct = default)
            => Task.FromResult(lines);
    }
}
