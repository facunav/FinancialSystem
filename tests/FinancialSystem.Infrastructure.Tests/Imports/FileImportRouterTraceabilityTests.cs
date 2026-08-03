using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Imports;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Infrastructure.Imports;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Imports;

/// <summary>
/// Cubre la trazabilidad agregada a FileImportRouter/ImportBatch en el Patch 0054 (Epic P,
/// P005): cada corrida debe quedar consultable en el historial con su estado final
/// (Exitosa / Exitosa con advertencias / Rechazada / Fallida), el parser específico que la
/// procesó, su duración y sus métricas (detectados/persistidos/omitidos/errores)
/// calculadas de forma centralizada -- nunca por cada handler. Mismo patrón InMemory que
/// FileImportRouterConsistencyTests (Patch 0053) y FileImportRouterIdempotencyTests
/// (Patch 0052).
/// </summary>
public class FileImportRouterTraceabilityTests
{
    [Fact]
    public async Task SuccessfulImport_PersistsProcessedOutcomeWithParserAndFileSize()
    {
        var path = CreateTempFile("contenido-ok"u8.ToArray(), $"{Guid.NewGuid():N}_ok.csv");
        var dbName = Guid.NewGuid().ToString();
        try
        {
            var handler = new ConfigurableHandler(inserted: 4, duplicates: 0, failed: 0, skipped: 0, parserUsed: "CsvFileParser");
            var router = CreateRouter(dbName, handler, new AlwaysValidValidator());

            await router.RouteAsync(path);

            await using var db = OpenDb(dbName);
            var batch = await db.ImportBatches.SingleAsync();

            Assert.Equal(ImportBatchOutcome.Processed, batch.Outcome);
            Assert.Equal("CsvFileParser", batch.ParserUsed);
            Assert.NotNull(batch.FileSizeBytes);
            Assert.True(batch.FileSizeBytes > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RejectedImport_PersistsRejectedByValidationOutcome()
    {
        var dbName = Guid.NewGuid().ToString();
        var handler = new ConfigurableHandler(inserted: 0, duplicates: 0, failed: 0, skipped: 0, parserUsed: null);
        var router = CreateRouter(dbName, handler, new InvalidValidator());

        await router.RouteAsync("archivo_invalido.csv");

        await using var db = OpenDb(dbName);
        var batch = await db.ImportBatches.SingleAsync();

        Assert.Equal(ImportBatchOutcome.RejectedByValidation, batch.Outcome);
        Assert.Null(batch.ParserUsed);
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public async Task FailedImport_PersistsFailedOutcome()
    {
        var path = CreateTempFile("contenido-que-falla"u8.ToArray(), $"{Guid.NewGuid():N}_falla.csv");
        var dbName = Guid.NewGuid().ToString();
        try
        {
            var handler = new ThrowingHandler();
            var router = CreateRouter(dbName, handler, new AlwaysValidValidator());

            await router.RouteAsync(path);

            await using var db = OpenDb(dbName);
            var batch = await db.ImportBatches.SingleAsync();

            Assert.Equal(ImportBatchOutcome.Failed, batch.Outcome);
            Assert.Equal(1, batch.FailedCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MetricsAreCalculatedCentrally_DetectedPersistedOmittedAndErrorsAreConsistent()
    {
        // 5 insertados + 2 duplicados + 3 salteados + 1 fallido: el handler solo reporta
        // los 4 contadores crudos -- Detected/Persisted/Omitted/Error los calcula
        // ImportBatch, en un único lugar, no cada parser.
        var path = CreateTempFile("contenido-con-advertencias"u8.ToArray(), $"{Guid.NewGuid():N}_advertencias.csv");
        var dbName = Guid.NewGuid().ToString();
        try
        {
            var handler = new ConfigurableHandler(inserted: 5, duplicates: 2, failed: 1, skipped: 3, parserUsed: "ExcelWorkbookParser");
            var router = CreateRouter(dbName, handler, new AlwaysValidValidator());

            await router.RouteAsync(path);

            await using var db = OpenDb(dbName);
            var batch = await db.ImportBatches.SingleAsync();

            Assert.Equal(11, batch.DetectedCount); // 5 + 2 + 3 + 1
            Assert.Equal(5, batch.PersistedCount);
            Assert.Equal(5, batch.OmittedCount); // 2 duplicados + 3 salteados
            Assert.Equal(1, batch.ErrorCount);

            // Al menos una fila omitida/fallida sobre una corrida procesada -> advertencia,
            // no una falla completa ni un rechazo.
            Assert.Equal(ImportBatchOutcome.ProcessedWithWarnings, batch.Outcome);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ParserUsed_IsRegisteredFromTheHandlerThatActuallyProcessedTheFile()
    {
        var path = CreateTempFile("contenido-parser"u8.ToArray(), $"{Guid.NewGuid():N}_parser.csv");
        var dbName = Guid.NewGuid().ToString();
        try
        {
            var handler = new ConfigurableHandler(inserted: 1, duplicates: 0, failed: 0, skipped: 0, parserUsed: "BBVA_VISA_AR");
            var router = CreateRouter(dbName, handler, new AlwaysValidValidator());

            await router.RouteAsync(path);

            await using var db = OpenDb(dbName);
            var batch = await db.ImportBatches.SingleAsync();

            Assert.Equal("BBVA_VISA_AR", batch.ParserUsed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Duration_IsCalculatedFromStartAndCompletionTimestamps()
    {
        var path = CreateTempFile("contenido-duracion"u8.ToArray(), $"{Guid.NewGuid():N}_duracion.csv");
        var dbName = Guid.NewGuid().ToString();
        try
        {
            var handler = new ConfigurableHandler(inserted: 1, duplicates: 0, failed: 0, skipped: 0, parserUsed: "CsvFileParser");
            var clock = new IncrementingDateTimeProvider(
                start: new DateTime(2026, 8, 3, 10, 0, 0, DateTimeKind.Utc),
                increment: TimeSpan.FromSeconds(5));

            var scopeFactory = BuildServices(dbName).GetRequiredService<IServiceScopeFactory>();
            var router = new FileImportRouter(
                [handler],
                new AlwaysValidValidator(),
                scopeFactory,
                clock,
                NullLogger<FileImportRouter>.Instance);

            await router.RouteAsync(path);

            await using var db = OpenDb(dbName);
            var batch = await db.ImportBatches.SingleAsync();

            Assert.Equal(new DateTime(2026, 8, 3, 10, 0, 0, DateTimeKind.Utc), batch.StartedAtUtc);
            Assert.Equal(new DateTime(2026, 8, 3, 10, 0, 5, DateTimeKind.Utc), batch.CompletedAtUtc);
            Assert.Equal(TimeSpan.FromSeconds(5), batch.Duration);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task HistoryStaysConsistentAcrossMultipleImports()
    {
        var dbName = Guid.NewGuid().ToString();

        var okPath = CreateTempFile("contenido-multi-ok"u8.ToArray(), $"{Guid.NewGuid():N}_multi_ok.csv");
        var warnPath = CreateTempFile("contenido-multi-warn"u8.ToArray(), $"{Guid.NewGuid():N}_multi_warn.csv");
        try
        {
            var okHandler = new ConfigurableHandler(inserted: 3, duplicates: 0, failed: 0, skipped: 0, parserUsed: "CsvFileParser");
            var okRouter = CreateRouter(dbName, okHandler, new AlwaysValidValidator());
            await okRouter.RouteAsync(okPath);

            var warnHandler = new ConfigurableHandler(inserted: 2, duplicates: 0, failed: 0, skipped: 1, parserUsed: "ExcelWorkbookParser");
            var warnRouter = CreateRouter(dbName, warnHandler, new AlwaysValidValidator());
            await warnRouter.RouteAsync(warnPath);

            var rejectedRouter = CreateRouter(dbName, new ConfigurableHandler(0, 0, 0, 0, null), new InvalidValidator());
            await rejectedRouter.RouteAsync("rechazado_multi.csv");

            var failedHandler = new ThrowingHandler();
            var failedPath = CreateTempFile("contenido-multi-falla"u8.ToArray(), $"{Guid.NewGuid():N}_multi_falla.csv");
            try
            {
                var failedRouter = CreateRouter(dbName, failedHandler, new AlwaysValidValidator());
                await failedRouter.RouteAsync(failedPath);

                await using var db = OpenDb(dbName);
                var batches = await db.ImportBatches.ToListAsync();
                Assert.Equal(4, batches.Count);

                var ok = batches.Single(b => b.ParserUsed == "CsvFileParser");
                var warn = batches.Single(b => b.ParserUsed == "ExcelWorkbookParser");
                var rejected = batches.Single(b => b.HandlerName == "Validation");
                var failed = batches.Single(b => b.HandlerName == failedHandler.HandlerName);

                Assert.Equal(ImportBatchOutcome.Processed, ok.Outcome);
                Assert.Equal(ImportBatchOutcome.ProcessedWithWarnings, warn.Outcome);
                Assert.Equal(ImportBatchOutcome.RejectedByValidation, rejected.Outcome);
                Assert.Equal(ImportBatchOutcome.Failed, failed.Outcome);
            }
            finally
            {
                File.Delete(failedPath);
            }
        }
        finally
        {
            File.Delete(okPath);
            File.Delete(warnPath);
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

    private static FileImportRouter CreateRouter(string dbName, IFileImportHandler handler, IImportFileValidator validator) =>
        new(
            [handler],
            validator,
            BuildServices(dbName).GetRequiredService<IServiceScopeFactory>(),
            new FakeDateTimeProvider(),
            NullLogger<FileImportRouter>.Instance);

    private static AppDbContext OpenDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private sealed class AlwaysValidValidator : IImportFileValidator
    {
        public ImportFileValidationResult Validate(string filePath) => ImportFileValidationResult.Valid;
    }

    private sealed class InvalidValidator : IImportFileValidator
    {
        public ImportFileValidationResult Validate(string filePath) =>
            ImportFileValidationResult.Invalid("Archivo inválido de prueba.");
    }

    /// <summary>Handler configurable: reporta exactamente los contadores y el ParserUsed que se le pidan.</summary>
    private sealed class ConfigurableHandler(int inserted, int duplicates, int failed, int skipped, string? parserUsed)
        : IFileImportHandler
    {
        public int InvocationCount { get; private set; }

        public string HandlerName => "Configurable";

        public bool CanHandle(string filePath) => true;

        public Task<ImportRunResult> HandleAsync(string filePath, CancellationToken ct = default)
        {
            InvocationCount++;
            return Task.FromResult(new ImportRunResult(inserted, duplicates, failed, skipped, [], ParserUsed: parserUsed));
        }
    }

    private sealed class ThrowingHandler : IFileImportHandler
    {
        public string HandlerName => "ThrowingHandler";

        public bool CanHandle(string filePath) => true;

        public Task<ImportRunResult> HandleAsync(string filePath, CancellationToken ct = default) =>
            throw new InvalidOperationException("Fallo simulado dentro del handler.");
    }

    private sealed class FakeDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>
    /// Reloj que avanza un incremento fijo en cada lectura -- permite que StartedAtUtc y
    /// CompletedAtUtc (dos llamadas a UtcNow separadas dentro de RouteAsync) difieran en un
    /// valor exacto y conocido, para poder verificar Duration sin depender del reloj real.
    /// </summary>
    private sealed class IncrementingDateTimeProvider(DateTime start, TimeSpan increment) : IDateTimeProvider
    {
        private DateTime _current = start;

        public DateTime UtcNow
        {
            get
            {
                var value = _current;
                _current += increment;
                return value;
            }
        }
    }
}
