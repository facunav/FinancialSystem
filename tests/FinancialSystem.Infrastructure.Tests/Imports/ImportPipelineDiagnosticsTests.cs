using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Imports;
using FinancialSystem.Infrastructure.Imports;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Imports;

/// <summary>
/// Cubre la observabilidad y el diagnóstico del pipeline de importación agregados en el
/// Patch 0057 (Epic P, P008): FileImportRouter llama exactamente una vez a
/// IImportPipelineDiagnostics.RecordRun por cada RouteAsync (con la duración total, la
/// duración de cada etapa propia del router y los indicadores de calidad derivados de
/// ImportRunResult) y, cuando una excepción escapa de una etapa, llama a RecordFailure con
/// la etapa exacta donde ocurrió -- una única vez por incidente, sin importar cuán
/// anidado esté el manejo de errores. Usa un IImportPipelineDiagnostics espía (no un
/// logger real) para poder inspeccionar las llamadas.
/// </summary>
public class ImportPipelineDiagnosticsTests
{
    [Fact]
    public async Task SuccessfulImport_RecordsRunMetricsWithAllStagesAndSuccessfulIndicators()
    {
        var path = CreateTempFile("contenido-ok"u8.ToArray(), $"{Guid.NewGuid():N}.csv");
        var dbName = Guid.NewGuid().ToString();
        var diagnostics = new SpyPipelineDiagnostics();
        try
        {
            var handler = new ConfigurableHandler(inserted: 3, duplicates: 0, failed: 0, skipped: 0);
            var router = CreateRouter(dbName, handler, new AlwaysValidValidator(), diagnostics);

            await router.RouteAsync(path);

            var metrics = Assert.Single(diagnostics.Runs);
            Assert.Equal(5, metrics.StageDurations.Count);
            Assert.True(metrics.Indicators.Successful);
            Assert.False(metrics.Indicators.Failed);
            Assert.False(metrics.Indicators.Rejected);
            Assert.Equal(3, metrics.Indicators.MovementsPersisted);
            Assert.Empty(diagnostics.Failures);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FailedImportViaThrowingHandler_RecordsRunMetricsAndFailureAtExtractionStage()
    {
        var path = CreateTempFile("contenido-que-falla"u8.ToArray(), $"{Guid.NewGuid():N}.csv");
        var dbName = Guid.NewGuid().ToString();
        var diagnostics = new SpyPipelineDiagnostics();
        try
        {
            var handler = new ThrowingHandler();
            var router = CreateRouter(dbName, handler, new AlwaysValidValidator(), diagnostics);

            await router.RouteAsync(path);

            var metrics = Assert.Single(diagnostics.Runs);
            Assert.True(metrics.Indicators.Failed);
            Assert.False(metrics.Indicators.Successful);

            var failure = Assert.Single(diagnostics.Failures);
            Assert.Equal(ImportPipelineStage.Extraction, failure.Stage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ErrorDuringRouterOrchestration_RecordsFailureAtValidationStage()
    {
        // Un fallo antes de llegar a ofrecer el archivo a cualquier handler -- distinto de
        // la etapa Extraction del test anterior -- prueba que la etapa reportada es la
        // que realmente estaba en curso, no un valor fijo.
        var dbName = Guid.NewGuid().ToString();
        var diagnostics = new SpyPipelineDiagnostics();
        var handler = new ConfigurableHandler(inserted: 0, duplicates: 0, failed: 0, skipped: 0);
        var router = CreateRouter(dbName, handler, new ThrowingValidator(), diagnostics);

        await router.RouteAsync("cualquier_archivo.csv");

        var failure = Assert.Single(diagnostics.Failures);
        Assert.Equal(ImportPipelineStage.Validation, failure.Stage);

        var metrics = Assert.Single(diagnostics.Runs);
        Assert.True(metrics.Indicators.Failed);
    }

    [Fact]
    public async Task HandlerThatGracefullyReturnsFailure_DoesNotEmitAFailureEvent()
    {
        // Un handler que captura su propia excepción y devuelve ImportRunResult.Failure()
        // (en vez de dejarla escapar) no es un "incidente no controlado" del pipeline --
        // RecordFailure es exclusivamente para excepciones que el router mismo tuvo que
        // atajar. Ambos caminos terminan en Outcome=Failed, pero solo uno es un evento de
        // diagnóstico de falla.
        var path = CreateTempFile("contenido"u8.ToArray(), $"{Guid.NewGuid():N}.csv");
        var dbName = Guid.NewGuid().ToString();
        var diagnostics = new SpyPipelineDiagnostics();
        try
        {
            var handler = new GracefullyFailingHandler();
            var router = CreateRouter(dbName, handler, new AlwaysValidValidator(), diagnostics);

            await router.RouteAsync(path);

            Assert.Empty(diagnostics.Failures);
            var metrics = Assert.Single(diagnostics.Runs);
            Assert.True(metrics.Indicators.Failed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EveryOutcome_EmitsExactlyOneRunMetricsEvent_NoDuplicates()
    {
        // Rechazo, éxito y falla -- cada RouteAsync deja exactamente un evento de
        // métricas, nunca cero ni más de uno, sin importar el desenlace.
        var dbName = Guid.NewGuid().ToString();
        var diagnostics = new SpyPipelineDiagnostics();

        var rejectedRouter = CreateRouter(dbName, new ConfigurableHandler(0, 0, 0, 0), new InvalidValidator(), diagnostics);
        await rejectedRouter.RouteAsync("rechazado.csv");

        var okPath = CreateTempFile("contenido-ok"u8.ToArray(), $"{Guid.NewGuid():N}_ok.csv");
        var failedPath = CreateTempFile("contenido-fallo"u8.ToArray(), $"{Guid.NewGuid():N}_fallo.csv");
        try
        {
            var okRouter = CreateRouter(dbName, new ConfigurableHandler(2, 0, 0, 0), new AlwaysValidValidator(), diagnostics);
            await okRouter.RouteAsync(okPath);

            var failedRouter = CreateRouter(dbName, new ThrowingHandler(), new AlwaysValidValidator(), diagnostics);
            await failedRouter.RouteAsync(failedPath);

            Assert.Equal(3, diagnostics.Runs.Count);
            Assert.Single(diagnostics.Failures);
        }
        finally
        {
            File.Delete(okPath);
            File.Delete(failedPath);
        }
    }

    [Fact]
    public void QualityIndicators_AreComputedUniformlyRegardlessOfWhichImporterProducedTheResult()
    {
        // Misma forma de ImportRunResult (mismos contadores), producida por dos handlers
        // que representan orígenes distintos (ParserUsed/HandlerName distintos) -- los
        // indicadores deben ser idénticos, porque se calculan solo a partir de
        // Inserted/Duplicates/Failed/Skipped/Outcome (el contrato único del Patch 0055),
        // nunca del origen del archivo.
        var fromBbva = new ImportRunResult(4, 1, 0, 2, [], ParserUsed: "BbvaBankStatementParser");
        var fromCatchAll = new ImportRunResult(4, 1, 0, 2, [], ParserUsed: "CsvFileParser");

        var bbvaIndicators = ImportPipelineQualityIndicators.FromResult(fromBbva);
        var catchAllIndicators = ImportPipelineQualityIndicators.FromResult(fromCatchAll);

        Assert.Equal(bbvaIndicators, catchAllIndicators);
        Assert.Equal(4, bbvaIndicators.MovementsPersisted);
        Assert.Equal(3, bbvaIndicators.MovementsOmitted); // 1 duplicado + 2 salteados
    }

    private static string CreateTempFile(byte[] content, string fileName)
    {
        var path = Path.Combine(Path.GetTempPath(), fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static FileImportRouter CreateRouter(
        string dbName, IFileImportHandler handler, IImportFileValidator validator, IImportPipelineDiagnostics diagnostics)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new FileImportRouter(
            [handler],
            validator,
            new NoOpConsistencyVerifier(),
            diagnostics,
            scopeFactory,
            new FakeDateTimeProvider(),
            NullLogger<FileImportRouter>.Instance);
    }

    private sealed class AlwaysValidValidator : IImportFileValidator
    {
        public ImportFileValidationResult Validate(string filePath) => ImportFileValidationResult.Valid;
    }

    private sealed class InvalidValidator : IImportFileValidator
    {
        public ImportFileValidationResult Validate(string filePath) =>
            ImportFileValidationResult.Invalid("Archivo inválido de prueba.");
    }

    private sealed class ThrowingValidator : IImportFileValidator
    {
        public ImportFileValidationResult Validate(string filePath) =>
            throw new InvalidOperationException("Fallo simulado antes de cualquier procesamiento.");
    }

    private sealed class NoOpConsistencyVerifier : IImportConsistencyVerifier
    {
        public Task<ImportConsistencyReport> VerifyAsync(ImportConsistencyContext context, CancellationToken ct = default) =>
            Task.FromResult(ImportConsistencyReport.Consistent);
    }

    private sealed class FakeDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);
    }

    private sealed class ConfigurableHandler(int inserted, int duplicates, int failed, int skipped) : IFileImportHandler
    {
        public string HandlerName => "Configurable";

        public bool CanHandle(string filePath) => true;

        public Task<ImportRunResult> HandleAsync(string filePath, Guid importBatchId, CancellationToken ct = default) =>
            Task.FromResult(new ImportRunResult(inserted, duplicates, failed, skipped, []));
    }

    private sealed class ThrowingHandler : IFileImportHandler
    {
        public string HandlerName => "ThrowingHandler";

        public bool CanHandle(string filePath) => true;

        public Task<ImportRunResult> HandleAsync(string filePath, Guid importBatchId, CancellationToken ct = default) =>
            throw new InvalidOperationException("Fallo simulado dentro del handler.");
    }

    /// <summary>Captura su propia excepción y devuelve Failure() -- nunca deja escapar nada al router.</summary>
    private sealed class GracefullyFailingHandler : IFileImportHandler
    {
        public string HandlerName => "GracefullyFailing";

        public bool CanHandle(string filePath) => true;

        public Task<ImportRunResult> HandleAsync(string filePath, Guid importBatchId, CancellationToken ct = default)
        {
            try
            {
                throw new InvalidOperationException("Fallo interno simulado.");
            }
            catch (Exception ex)
            {
                return Task.FromResult(ImportRunResult.Failure($"No se pudo procesar: {ex.Message}"));
            }
        }
    }

    private sealed class SpyPipelineDiagnostics : IImportPipelineDiagnostics
    {
        public List<ImportPipelineRunMetrics> Runs { get; } = [];
        public List<(string SourceFile, Guid? ImportBatchId, ImportPipelineStage Stage, Exception Exception)> Failures { get; } = [];

        public void RecordRun(ImportPipelineRunMetrics metrics) => Runs.Add(metrics);

        public void RecordFailure(string sourceFile, Guid? importBatchId, ImportPipelineStage stage, Exception exception) =>
            Failures.Add((sourceFile, importBatchId, stage, exception));
    }
}
