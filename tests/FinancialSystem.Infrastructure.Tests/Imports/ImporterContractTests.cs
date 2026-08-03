using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Imports;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Infrastructure.Imports;
using FinancialSystem.Infrastructure.Imports.BankStatements;
using FinancialSystem.Infrastructure.Imports.Normalization;
using FinancialSystem.Infrastructure.Imports.Parsers;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Imports;

/// <summary>
/// Cubre la unificación del contrato entre FileImportRouter y los IFileImportHandler en
/// el Patch 0055 (Epic P, P006).
///
/// HALLAZGO CORREGIDO POR ESTE PATCH:
///   BbvaBankStatementImportHandler era el único de los tres handlers registrados que,
///   ante un archivo que no podía siquiera abrirse/leerse (XLS corrupto, borrado justo
///   después de pasar IImportFileValidator), no reportaba Outcome=Failed -- lo convertía
///   en un ImportRunResult "silencioso" (Inserted=0, Failed=1) que FileImportRouter
///   terminaba clasificando como ProcessedWithWarnings ("Exitosa con advertencias"),
///   ocultando que en realidad no se procesó nada. BbvaDebitCardEnrichmentHandler e
///   ImportFileProcessingSink ya reportaban este mismo tipo de falla como Failure()
///   explícito. Los tests de este archivo prueban que los TRES handlers reales ahora
///   coinciden, y que el contrato (ImportRunResult + FileImportRouter.ClassifyOutcome)
///   es indiferente a qué IFileImportHandler concreto lo produjo -- incluyendo uno nuevo,
///   nunca antes visto por el pipeline, sin ningún cambio en FileImportRouter.
/// </summary>
public class ImporterContractTests
{
    // ── Errores reportados de forma consistente entre los 3 handlers reales ──────

    [Fact]
    public async Task RealBbvaBankStatementHandler_UnrecoverableReadFailure_ReportsFailedOutcome()
    {
        // "Caja_test.xls" matchea BbvaBankStatementFilePatterns, pero el contenido no es
        // un XLS/XLSX real -- pasa IImportFileValidator (existe, no vacío, se puede leer
        // como bytes) pero XlsBankStatementReader.ReadFirstSheet falla al interpretarlo.
        var path = CreateTempFile("esto no es un archivo xls válido"u8.ToArray(), $"Caja_{Guid.NewGuid():N}.xls");
        var dbName = Guid.NewGuid().ToString();
        try
        {
            var reader = new XlsBankStatementReader(NullLogger<XlsBankStatementReader>.Instance);
            var parser = new BbvaBankStatementParser(NullLogger<BbvaBankStatementParser>.Instance);
            var importer = new BbvaBankStatementImporter(
                reader, parser, null!, NullLogger<BbvaBankStatementImporter>.Instance);
            var handler = new BbvaBankStatementImportHandler(
                importer, Options.Create(new FileIngestionOptions()),
                NullLogger<BbvaBankStatementImportHandler>.Instance);

            var router = CreateRouter(dbName, handler);
            await router.RouteAsync(path);

            await using var db = OpenDb(dbName);
            var batch = await db.ImportBatches.SingleAsync();
            Assert.Equal(ImportBatchOutcome.Failed, batch.Outcome);
            Assert.Equal(0, batch.InsertedCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RealBbvaDebitCardHandler_UnrecoverableReadFailure_ReportsFailedOutcome()
    {
        var path = CreateTempFile("esto no es un archivo xlsx válido"u8.ToArray(), $"Ultimos_movimientos_{Guid.NewGuid():N}.xlsx");
        var dbName = Guid.NewGuid().ToString();
        try
        {
            var reader = new XlsBankStatementReader(NullLogger<XlsBankStatementReader>.Instance);
            var parser = new BbvaDebitCardParser(NullLogger<BbvaDebitCardParser>.Instance);
            var handler = new BbvaDebitCardEnrichmentHandler(
                reader, parser, null!, Options.Create(new FileIngestionOptions()),
                NullLogger<BbvaDebitCardEnrichmentHandler>.Instance);

            var router = CreateRouter(dbName, handler);
            await router.RouteAsync(path);

            await using var db = OpenDb(dbName);
            var batch = await db.ImportBatches.SingleAsync();
            Assert.Equal(ImportBatchOutcome.Failed, batch.Outcome);
            Assert.Equal(0, batch.InsertedCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RealTransactionCatchAllHandler_UnrecoverableReadFailure_ReportsFailedOutcome()
    {
        // Mismo tipo de falla (contenido no parseable) pero a través del catch-all
        // Transaction -> ImportFileProcessingSink -> ExcelWorkbookParser real.
        var path = CreateTempFile("esto no es un archivo xlsx válido"u8.ToArray(), $"extracto_generico_{Guid.NewGuid():N}.xlsx");
        var dbName = Guid.NewGuid().ToString();
        try
        {
            var services = BuildServices(dbName);
            var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
            var sink = new ImportFileProcessingSink(
                new FakeFileParserFactory(new ExcelWorkbookParser(NullLogger<ExcelWorkbookParser>.Instance)),
                new TransactionNormalizer(),
                scopeFactory,
                new FakeDateTimeProvider(),
                NullLogger<ImportFileProcessingSink>.Instance);
            var handler = new TransactionImportHandler(sink, NullLogger<TransactionImportHandler>.Instance);

            var router = CreateRouter(scopeFactory, handler);
            await router.RouteAsync(path);

            await using var db = OpenDb(dbName);
            var batch = await db.ImportBatches.SingleAsync();
            Assert.Equal(ImportBatchOutcome.Failed, batch.Outcome);
            Assert.Equal(0, batch.InsertedCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── Extensibilidad: un importador nuevo, sin ningún cambio en FileImportRouter ──

    [Fact]
    public async Task NewCustomImportHandler_SuccessfulRun_IntegratesWithoutAnyPipelineChange()
    {
        var path = CreateTempFile("contenido-nuevo-importador"u8.ToArray(), $"{Guid.NewGuid():N}.ofx");
        var dbName = Guid.NewGuid().ToString();
        try
        {
            var handler = new HypotheticalOfxImportHandler(inserted: 3, duplicates: 0, failed: 0, skipped: 0);
            var router = CreateRouter(dbName, handler);

            await router.RouteAsync(path);

            await using var db = OpenDb(dbName);
            var batch = await db.ImportBatches.SingleAsync();
            Assert.Equal(ImportBatchOutcome.Processed, batch.Outcome);
            Assert.Equal("HypotheticalOfxParser", batch.ParserUsed);
            Assert.Equal(3, batch.PersistedCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task NewCustomImportHandler_RunWithSkippedRows_IsClassifiedAsProcessedWithWarnings()
    {
        var path = CreateTempFile("contenido-con-omitidos"u8.ToArray(), $"{Guid.NewGuid():N}.ofx");
        var dbName = Guid.NewGuid().ToString();
        try
        {
            var handler = new HypotheticalOfxImportHandler(inserted: 5, duplicates: 0, failed: 0, skipped: 2);
            var router = CreateRouter(dbName, handler);

            await router.RouteAsync(path);

            await using var db = OpenDb(dbName);
            var batch = await db.ImportBatches.SingleAsync();

            // Mismo criterio centralizado que cualquier otro handler (Patch 0054): el
            // router decide la advertencia a partir de los contadores crudos, sin que
            // este importador nuevo tenga que saber nada de ProcessedWithWarnings.
            Assert.Equal(ImportBatchOutcome.ProcessedWithWarnings, batch.Outcome);
            Assert.Equal(2, batch.OmittedCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task NewCustomImportHandler_RunThatThrows_IsClassifiedAsFailedJustLikeAnyOtherHandler()
    {
        var path = CreateTempFile("contenido-que-hace-fallar-al-importador"u8.ToArray(), $"{Guid.NewGuid():N}.ofx");
        var dbName = Guid.NewGuid().ToString();
        try
        {
            var handler = new ThrowingOfxImportHandler();
            var router = CreateRouter(dbName, handler);

            await router.RouteAsync(path);

            await using var db = OpenDb(dbName);
            var batch = await db.ImportBatches.SingleAsync();
            Assert.Equal(ImportBatchOutcome.Failed, batch.Outcome);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTempFile(byte[] content, string fileName)
    {
        var path = Path.Combine(Path.GetTempPath(), fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static ServiceProvider BuildServices(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        return services.BuildServiceProvider();
    }

    private static FileImportRouter CreateRouter(string dbName, IFileImportHandler handler) =>
        CreateRouter(BuildServices(dbName).GetRequiredService<IServiceScopeFactory>(), handler);

    private static FileImportRouter CreateRouter(IServiceScopeFactory scopeFactory, IFileImportHandler handler) =>
        new(
            [handler],
            new AlwaysValidValidator(),
            new NoOpConsistencyVerifier(),
            new NoOpPipelineDiagnostics(),
            scopeFactory,
            new FakeDateTimeProvider(),
            NullLogger<FileImportRouter>.Instance);

    private static AppDbContext OpenDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private sealed class AlwaysValidValidator : IImportFileValidator
    {
        public ImportFileValidationResult Validate(string filePath) => ImportFileValidationResult.Valid;
    }

    private sealed class FakeFileParserFactory(IFileParser parser) : IFileParserFactory
    {
        public FileParserResolution ResolveParser(string filePath) => FileParserResolution.Resolved(parser);
    }

    private sealed class FakeDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>Verificador de integridad (Patch 0056) neutralizado -- estos tests no cubren esa etapa.</summary>
    private sealed class NoOpConsistencyVerifier : IImportConsistencyVerifier
    {
        public Task<ImportConsistencyReport> VerifyAsync(ImportConsistencyContext context, CancellationToken ct = default) =>
            Task.FromResult(ImportConsistencyReport.Consistent);
    }

    /// <summary>Diagnóstico del pipeline (Patch 0057) neutralizado -- estos tests no cubren esa etapa.</summary>
    private sealed class NoOpPipelineDiagnostics : IImportPipelineDiagnostics
    {
        public void RecordRun(ImportPipelineRunMetrics metrics) { }

        public void RecordFailure(string sourceFile, Guid? importBatchId, ImportPipelineStage stage, Exception exception) { }
    }

    /// <summary>
    /// Simula un importador agregado después de este patch (ej.: soporte para extractos
    /// OFX) -- implementa IFileImportHandler sin que FileImportRouter necesite conocerlo
    /// de ninguna forma especial, probando el objetivo de extensibilidad del patch.
    /// </summary>
    private sealed class HypotheticalOfxImportHandler(int inserted, int duplicates, int failed, int skipped)
        : IFileImportHandler
    {
        public string HandlerName => "HypotheticalOfx";

        public bool CanHandle(string filePath) => Path.GetExtension(filePath).Equals(".ofx", StringComparison.OrdinalIgnoreCase);

        public Task<ImportRunResult> HandleAsync(string filePath, CancellationToken ct = default) =>
            Task.FromResult(new ImportRunResult(
                inserted, duplicates, failed, skipped, [], ParserUsed: "HypotheticalOfxParser"));
    }

    private sealed class ThrowingOfxImportHandler : IFileImportHandler
    {
        public string HandlerName => "ThrowingOfx";

        public bool CanHandle(string filePath) => Path.GetExtension(filePath).Equals(".ofx", StringComparison.OrdinalIgnoreCase);

        public Task<ImportRunResult> HandleAsync(string filePath, CancellationToken ct = default) =>
            throw new InvalidOperationException("Fallo simulado de un importador nuevo.");
    }
}
