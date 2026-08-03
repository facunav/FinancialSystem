using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Imports;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Infrastructure.Imports;
using FinancialSystem.Infrastructure.Imports.Consistency;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Imports;

/// <summary>
/// Cubre la verificación de integridad posterior a la importación agregada en el Patch
/// 0056 (Epic P, P007): después de persistir un ImportBatch, IImportConsistencyVerifier
/// vuelve a leer lo persistido y confirma que coincide con lo que la corrida reportó --
/// cantidades (QuantityConsistencyCheck), identidad/procedencia de los movimientos
/// (MovementProvenanceCheck) e integridad del propio registro de historial
/// (ImportHistoryConsistencyCheck). Cuando encuentra una inconsistencia, la marca en el
/// mismo ImportBatch (ConsistencyVerified=false + ConsistencyIssues) sin ocultarla y sin
/// alterar InsertedCount/Outcome/etc. de la corrida original.
/// </summary>
public class ImportConsistencyVerificationTests
{
    // ── QuantityConsistencyCheck ──────────────────────────────────────────────

    [Fact]
    public async Task QuantityCheck_CountsMatchWhatWasPersisted_ReturnsNoIssues()
    {
        var dbName = Guid.NewGuid().ToString();
        var batchId = Guid.NewGuid();
        await SeedImportBatchAsync(dbName, batchId, "Transaction", inserted: 5, duplicates: 1, failed: 0, skipped: 2, parserUsed: "CsvFileParser");

        var check = new QuantityConsistencyCheck(BuildScopeFactory(dbName));
        var context = BuildContext(batchId, "Transaction", expectedInserted: 5, expectedDuplicates: 1, expectedFailed: 0, expectedSkipped: 2, expectedParserUsed: "CsvFileParser");

        var issues = await check.CheckAsync(context);

        Assert.Empty(issues);
    }

    [Fact]
    public async Task QuantityCheck_PersistedCountsDifferFromWhatWasReported_ReturnsIssue()
    {
        var dbName = Guid.NewGuid().ToString();
        var batchId = Guid.NewGuid();
        // Lo persistido dice 5 insertados; el contexto (lo que el handler reportó) dice 7 --
        // simula que el registro en el historial no coincide con la corrida real.
        await SeedImportBatchAsync(dbName, batchId, "Transaction", inserted: 5, duplicates: 0, failed: 0, skipped: 0, parserUsed: "CsvFileParser");

        var check = new QuantityConsistencyCheck(BuildScopeFactory(dbName));
        var context = BuildContext(batchId, "Transaction", expectedInserted: 7, expectedDuplicates: 0, expectedFailed: 0, expectedSkipped: 0, expectedParserUsed: "CsvFileParser");

        var issues = await check.CheckAsync(context);

        Assert.NotEmpty(issues);
        Assert.Contains(issues, i => i.Contains("InsertedCount"));
    }

    // ── MovementProvenanceCheck ────────────────────────────────────────────────

    [Fact]
    public async Task ProvenanceCheck_ActualMovementsMatchReportedInsertedCount_ReturnsNoIssues()
    {
        var dbName = Guid.NewGuid().ToString();
        var startedAtUtc = new DateTime(2026, 8, 4, 10, 0, 0, DateTimeKind.Utc);
        var completedAtUtc = startedAtUtc.AddSeconds(5);
        var sourceFile = $"{Guid.NewGuid():N}.csv";

        await SeedTransactionsAsync(dbName, sourceFile, count: 3, createdAtUtc: startedAtUtc.AddSeconds(2));

        var check = new MovementProvenanceCheck(BuildScopeFactory(dbName));
        var context = new ImportConsistencyContext(
            Guid.NewGuid(), sourceFile, "Transaction", startedAtUtc, completedAtUtc,
            ExpectedInserted: 3, ExpectedDuplicates: 0, ExpectedFailed: 0, ExpectedSkipped: 0, ExpectedParserUsed: "CsvFileParser");

        var issues = await check.CheckAsync(context);

        Assert.Empty(issues);
    }

    [Fact]
    public async Task ProvenanceCheck_MorePersistedMovementsThanReported_ReturnsUnexplainedOriginIssue()
    {
        // "Movimiento persistido sin origen": el historial dice que se insertó 1, pero
        // realmente hay 2 Transactions con ese SourceFile dentro de la ventana de la
        // corrida -- una de ellas no tiene de dónde salió, según lo reportado.
        var dbName = Guid.NewGuid().ToString();
        var startedAtUtc = new DateTime(2026, 8, 4, 10, 0, 0, DateTimeKind.Utc);
        var completedAtUtc = startedAtUtc.AddSeconds(5);
        var sourceFile = $"{Guid.NewGuid():N}.csv";

        await SeedTransactionsAsync(dbName, sourceFile, count: 2, createdAtUtc: startedAtUtc.AddSeconds(2));

        var check = new MovementProvenanceCheck(BuildScopeFactory(dbName));
        var context = new ImportConsistencyContext(
            Guid.NewGuid(), sourceFile, "Transaction", startedAtUtc, completedAtUtc,
            ExpectedInserted: 1, ExpectedDuplicates: 0, ExpectedFailed: 0, ExpectedSkipped: 0, ExpectedParserUsed: "CsvFileParser");

        var issues = await check.CheckAsync(context);

        Assert.NotEmpty(issues);
        Assert.Contains(issues, i => i.Contains("sin origen"));
    }

    [Fact]
    public async Task ProvenanceCheck_FewerActualMovementsThanReported_ReturnsIssue()
    {
        // "Diferencia entre movimientos extraídos y persistidos": el handler dijo que
        // insertó 3, pero no hay ningún movimiento real con ese SourceFile.
        var dbName = Guid.NewGuid().ToString();
        var startedAtUtc = new DateTime(2026, 8, 4, 10, 0, 0, DateTimeKind.Utc);
        var completedAtUtc = startedAtUtc.AddSeconds(5);
        var sourceFile = $"{Guid.NewGuid():N}.csv";

        var check = new MovementProvenanceCheck(BuildScopeFactory(dbName));
        var context = new ImportConsistencyContext(
            Guid.NewGuid(), sourceFile, "Transaction", startedAtUtc, completedAtUtc,
            ExpectedInserted: 3, ExpectedDuplicates: 0, ExpectedFailed: 0, ExpectedSkipped: 0, ExpectedParserUsed: "CsvFileParser");

        var issues = await check.CheckAsync(context);

        Assert.NotEmpty(issues);
    }

    // ── ImportHistoryConsistencyCheck ──────────────────────────────────────────

    [Fact]
    public async Task HistoryCheck_ImportBatchMissingFromHistory_ReturnsIssue()
    {
        var dbName = Guid.NewGuid().ToString();
        var check = new ImportHistoryConsistencyCheck(BuildScopeFactory(dbName));
        var context = BuildContext(Guid.NewGuid(), "Transaction", 1, 0, 0, 0, "CsvFileParser");

        var issues = await check.CheckAsync(context);

        Assert.NotEmpty(issues);
        Assert.Contains(issues, i => i.Contains("no contiene ningún ImportBatch"));
    }

    [Fact]
    public async Task HistoryCheck_HandlerNameDoesNotMatchWhatWasPersisted_ReturnsIssue()
    {
        var dbName = Guid.NewGuid().ToString();
        var batchId = Guid.NewGuid();
        await SeedImportBatchAsync(dbName, batchId, "BbvaBankStatement", inserted: 1, duplicates: 0, failed: 0, skipped: 0, parserUsed: "BbvaBankStatementParser");

        var check = new ImportHistoryConsistencyCheck(BuildScopeFactory(dbName));
        var context = BuildContext(batchId, "Transaction", 1, 0, 0, 0, "BbvaBankStatementParser");

        var issues = await check.CheckAsync(context);

        Assert.NotEmpty(issues);
        Assert.Contains(issues, i => i.Contains("HandlerName"));
    }

    // ── Extremo a extremo, a través de FileImportRouter + verificador real ────

    [Fact]
    public async Task EndToEnd_ConsistentImport_MarksConsistencyVerifiedTrueWithNoIssues()
    {
        var dbName = Guid.NewGuid().ToString();
        var scopeFactory = BuildScopeFactory(dbName);
        var sourceFile = CreateTempFile("contenido-consistente"u8.ToArray(), $"{Guid.NewGuid():N}.csv");
        try
        {
            var handler = new PersistingHandler(scopeFactory, insertedCount: 3, parserUsed: "CsvFileParser");
            var router = CreateRouter(scopeFactory, handler);

            await router.RouteAsync(sourceFile);

            await using var db = OpenDb(dbName);
            var batch = await db.ImportBatches.SingleAsync();
            Assert.True(batch.ConsistencyVerified);
            Assert.Null(batch.ConsistencyIssues);
        }
        finally
        {
            File.Delete(sourceFile);
        }
    }

    [Fact]
    public async Task EndToEnd_InconsistentImport_MarksConsistencyVerifiedFalseWithIssues()
    {
        var dbName = Guid.NewGuid().ToString();
        var scopeFactory = BuildScopeFactory(dbName);
        var sourceFile = CreateTempFile("contenido-inconsistente"u8.ToArray(), $"{Guid.NewGuid():N}.csv");
        try
        {
            // Reporta 5 insertados pero no inserta ningún Transaction real -- exactamente
            // la clase de bug que esta verificación existe para detectar.
            var handler = new PersistingHandler(scopeFactory, insertedCount: 5, parserUsed: "CsvFileParser", actuallyPersist: false);
            var router = CreateRouter(scopeFactory, handler);

            await router.RouteAsync(sourceFile);

            await using var db = OpenDb(dbName);
            var batch = await db.ImportBatches.SingleAsync();
            Assert.False(batch.ConsistencyVerified);
            Assert.NotNull(batch.ConsistencyIssues);
        }
        finally
        {
            File.Delete(sourceFile);
        }
    }

    private static string CreateTempFile(byte[] content, string fileName)
    {
        var path = Path.Combine(Path.GetTempPath(), fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static IServiceScopeFactory BuildScopeFactory(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static AppDbContext OpenDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private static ImportConsistencyContext BuildContext(
        Guid batchId, string handlerName, int expectedInserted, int expectedDuplicates,
        int expectedFailed, int expectedSkipped, string? expectedParserUsed) =>
        new(
            batchId, "cualquier_archivo.csv", handlerName,
            new DateTime(2026, 8, 4, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 4, 10, 0, 5, DateTimeKind.Utc),
            expectedInserted, expectedDuplicates, expectedFailed, expectedSkipped, expectedParserUsed);

    private static async Task SeedImportBatchAsync(
        string dbName, Guid batchId, string handlerName, int inserted, int duplicates, int failed, int skipped, string? parserUsed)
    {
        await using var db = OpenDb(dbName);
        db.ImportBatches.Add(new ImportBatch
        {
            Id = batchId,
            SourceFile = "cualquier_archivo.csv",
            ContentHash = "hash-de-prueba",
            HandlerName = handlerName,
            StartedAtUtc = new DateTime(2026, 8, 4, 10, 0, 0, DateTimeKind.Utc),
            CompletedAtUtc = new DateTime(2026, 8, 4, 10, 0, 5, DateTimeKind.Utc),
            ParserUsed = parserUsed,
            InsertedCount = inserted,
            DuplicateCount = duplicates,
            FailedCount = failed,
            SkippedCount = skipped
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedTransactionsAsync(string dbName, string sourceFile, int count, DateTime createdAtUtc)
    {
        await using var db = OpenDb(dbName);
        for (var i = 0; i < count; i++)
        {
            db.Transactions.Add(new Transaction
            {
                Date = createdAtUtc,
                Description = $"movimiento-{i}",
                Amount = 10m,
                Currency = "ARS",
                CreatedAtUtc = createdAtUtc,
                ExternalId = Guid.NewGuid().ToString(),
                SourceFile = sourceFile
            });
        }
        await db.SaveChangesAsync();
    }

    private static FileImportRouter CreateRouter(IServiceScopeFactory scopeFactory, IFileImportHandler handler)
    {
        var checks = new IImportConsistencyCheck[]
        {
            new QuantityConsistencyCheck(scopeFactory),
            new MovementProvenanceCheck(scopeFactory),
            new ImportHistoryConsistencyCheck(scopeFactory),
        };
        var verifier = new ImportConsistencyVerifier(checks, NullLogger<ImportConsistencyVerifier>.Instance);

        return new FileImportRouter(
            [handler],
            new AlwaysValidValidator(),
            verifier,
            new NoOpPipelineDiagnostics(),
            scopeFactory,
            new FakeDateTimeProvider(),
            NullLogger<FileImportRouter>.Instance);
    }

    private sealed class AlwaysValidValidator : IImportFileValidator
    {
        public ImportFileValidationResult Validate(string filePath) => ImportFileValidationResult.Valid;
    }

    private sealed class FakeDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    /// <summary>Diagnóstico del pipeline (Patch 0057) neutralizado -- estos tests no cubren esa etapa.</summary>
    private sealed class NoOpPipelineDiagnostics : IImportPipelineDiagnostics
    {
        public void RecordRun(ImportPipelineRunMetrics metrics) { }

        public void RecordFailure(string sourceFile, Guid? importBatchId, ImportPipelineStage stage, Exception exception) { }
    }

    /// <summary>
    /// Handler que reporta Inserted=insertedCount y, si actuallyPersist es true, además
    /// inserta esa misma cantidad de Transaction reales con el SourceFile del archivo --
    /// para simular tanto una corrida genuinamente consistente como una que miente sobre
    /// lo que persistió.
    /// </summary>
    private sealed class PersistingHandler(
        IServiceScopeFactory scopeFactory, int insertedCount, string parserUsed, bool actuallyPersist = true)
        : IFileImportHandler
    {
        public string HandlerName => "Transaction";

        public bool CanHandle(string filePath) => true;

        public async Task<ImportRunResult> HandleAsync(string filePath, CancellationToken ct = default)
        {
            if (actuallyPersist)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                for (var i = 0; i < insertedCount; i++)
                {
                    db.Transactions.Add(new Transaction
                    {
                        Date = DateTime.UtcNow,
                        Description = $"movimiento-{i}",
                        Amount = 10m,
                        Currency = "ARS",
                        CreatedAtUtc = DateTime.UtcNow,
                        ExternalId = Guid.NewGuid().ToString(),
                        SourceFile = filePath
                    });
                }
                await db.SaveChangesAsync(ct);
            }

            return new ImportRunResult(insertedCount, 0, 0, 0, [], ParserUsed: parserUsed);
        }
    }
}
