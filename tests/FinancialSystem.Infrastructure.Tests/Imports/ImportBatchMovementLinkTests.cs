using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Imports;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Infrastructure.Imports;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Imports;

/// <summary>
/// Cubre el vínculo blando ImportBatchId agregado en Patch 0105: cada movimiento
/// insertado por una corrida de importación queda asociado, mediante una columna Guid?
/// simple (sin FK, sin navegación, sin constraint), al ImportBatch de esa misma corrida.
/// Mismo patrón InMemory que FileImportRouterTraceabilityTests (Patch 0054) e
/// ImporterContractTests (Patch 0055): fakes de IFileImportHandler que insertan
/// Transaction reales a través de su propio scope (igual que un importador real haría),
/// más un router real construido con las mismas dependencias neutralizadas
/// (NoOpConsistencyVerifier/NoOpPipelineDiagnostics) que ya usan esos archivos.
///
/// No cubre la mecánica de estampado en sí (eso ya lo prueban ImporterContractTests
/// contra los tres handlers reales, y la nueva
/// ImportFileProcessingSinkIdempotencyTests.HandleFileAsync_ReimportarElMismoArchivo_ConservaElImportBatchIdDeLaPrimeraCorrida
/// contra el sink real) -- este archivo cubre específicamente la garantía de punta a
/// punta: que FileImportRouter genera un único Guid por corrida y que ese mismo Guid
/// termina tanto en los movimientos insertados como en el ImportBatch persistido.
/// </summary>
public class ImportBatchMovementLinkTests
{
    [Fact]
    public async Task SuccessfulImport_AllInsertedMovements_ShareTheSameImportBatchId_MatchingThePersistedBatch()
    {
        var path = CreateTempFile("contenido-varios-movimientos"u8.ToArray(), $"{Guid.NewGuid():N}_varios.csv");
        var dbName = Guid.NewGuid().ToString();
        try
        {
            var services = BuildServices(dbName);
            var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
            var handler = new InsertingHandler(scopeFactory, movementCount: 3);
            var router = CreateRouter(scopeFactory, handler);

            await router.RouteAsync(path);

            await using var db = OpenDb(dbName);
            var batch = await db.ImportBatches.SingleAsync();
            var transactions = await db.Transactions.ToListAsync();

            Assert.Equal(3, transactions.Count);
            Assert.All(transactions, t => Assert.Equal(batch.Id, t.ImportBatchId));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TwoSeparateImports_ProduceDifferentImportBatchIds_NeverMixedBetweenRuns()
    {
        var dbName = Guid.NewGuid().ToString();
        var pathA = CreateTempFile("contenido-corrida-a"u8.ToArray(), $"{Guid.NewGuid():N}_a.csv");
        var pathB = CreateTempFile("contenido-corrida-b"u8.ToArray(), $"{Guid.NewGuid():N}_b.csv");
        try
        {
            var services = BuildServices(dbName);
            var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();

            var handlerA = new InsertingHandler(scopeFactory, movementCount: 1);
            await CreateRouter(scopeFactory, handlerA).RouteAsync(pathA);

            var handlerB = new InsertingHandler(scopeFactory, movementCount: 1);
            await CreateRouter(scopeFactory, handlerB).RouteAsync(pathB);

            await using var db = OpenDb(dbName);
            var batches = await db.ImportBatches.ToListAsync();
            Assert.Equal(2, batches.Count);
            Assert.NotEqual(batches[0].Id, batches[1].Id);

            var transactions = await db.Transactions.ToListAsync();
            Assert.Equal(2, transactions.Count);
            foreach (var transaction in transactions)
            {
                var owningBatch = batches.Single(b => b.Id == transaction.ImportBatchId);
                Assert.Equal(transaction.SourceFile, owningBatch.SourceFile);
            }
        }
        finally
        {
            File.Delete(pathA);
            File.Delete(pathB);
        }
    }

    [Fact]
    public async Task EnrichmentStyleHandler_NeverAssignsImportBatchId_ExistingMovementKeepsItsOriginalOne()
    {
        // No usa BbvaDebitCardEnrichmentHandler real (exige un XLSX real de tarjeta de
        // débito) -- reproduce, con un fake, la misma garantía estructural: un handler
        // que solo enriquece BankStatement existentes (no inserta nada) nunca toca
        // ImportBatchId. La cobertura del handler real en sí (parámetro no reenviado a
        // EnrichAsync) está en el propio código, ver BbvaDebitCardEnrichmentHandler.cs.
        var dbName = Guid.NewGuid().ToString();
        var originalBatchId = Guid.NewGuid();

        await using (var seedDb = OpenDb(dbName))
        {
            seedDb.BankStatements.Add(new BankStatement
            {
                Date = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                Concept = "PAGO CON VISA DEBITO 12345 OP1",
                Amount = -1000m,
                BankName = "BBVA",
                ExternalId = Guid.NewGuid().ToString(),
                ImportedAtUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                ImportBatchId = originalBatchId,
            });
            await seedDb.SaveChangesAsync();
        }

        var path = CreateTempFile("contenido-enriquecimiento"u8.ToArray(), $"{Guid.NewGuid():N}_enrich.xlsx");
        try
        {
            var services = BuildServices(dbName);
            var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
            var handler = new EnrichmentOnlyHandler(scopeFactory);
            var router = CreateRouter(scopeFactory, handler);

            await router.RouteAsync(path);

            await using var db = OpenDb(dbName);
            var statement = await db.BankStatements.SingleAsync();

            // El "enriquecimiento" cambió el Concept (representando Merchant/MerchantAtUtc
            // en el handler real); el ImportBatchId original nunca se tocó.
            Assert.Equal("ENRIQUECIDO", statement.Concept);
            Assert.Equal(originalBatchId, statement.ImportBatchId);

            var enrichmentBatch = await db.ImportBatches.SingleAsync();
            Assert.NotEqual(enrichmentBatch.Id, originalBatchId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PersistImportBatchAsyncFailure_MovementsRemainPersisted_WithUnresolvableImportBatchId()
    {
        // PATCH-0053 (ADR-005): un fallo al persistir ImportBatch se loguea pero no se
        // relanza -- los datos del handler ya quedaron guardados. Este test confirma que
        // ese comportamiento se conserva exactamente igual con el vínculo nuevo: el
        // movimiento sigue persistido, con su ImportBatchId ya estampado, aunque la fila
        // de ImportBatches nunca llegue a existir.
        var path = CreateTempFile("contenido-fallo-batch"u8.ToArray(), $"{Guid.NewGuid():N}_fallo_batch.csv");
        var dbName = Guid.NewGuid().ToString();
        try
        {
            // No se usa AddDbContext<AppDbContext, TImpl> (el overload de dos genéricos):
            // exigiría que FailOnImportBatchInsertDbContext acepte
            // DbContextOptions<FailOnImportBatchInsertDbContext>, un tipo distinto del que
            // recibe el constructor heredado de AppDbContext (DbContextOptions<AppDbContext>).
            // Registrar la instancia directamente, con las options ya tipadas para
            // AppDbContext, evita ese problema de invariancia de tipos genéricos.
            var services = new ServiceCollection();
            services.AddScoped<AppDbContext>(_ => new FailOnImportBatchInsertDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options));
            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
            var provider = services.BuildServiceProvider();
            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            var handler = new InsertingHandler(scopeFactory, movementCount: 1);
            var router = CreateRouter(scopeFactory, handler);

            // No debe lanzar: mismo comportamiento tolerante a fallos ya existente antes
            // de este patch (PersistImportBatchAsync atrapa la excepción y no la relanza).
            await router.RouteAsync(path);

            await using var db = OpenDb(dbName);

            // El movimiento quedó persistido (comportamiento de ADR-005/Patch 0053
            // preservado) con su ImportBatchId ya estampado...
            var transaction = await db.Transactions.SingleAsync();
            Assert.NotNull(transaction.ImportBatchId);

            // ...pero ningún ImportBatch existe con ese Id: consecuencia esperada y
            // aceptada de una referencia blanda, no un bug a corregir.
            Assert.False(await db.ImportBatches.AnyAsync(b => b.Id == transaction.ImportBatchId));
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
    /// Simula un handler que inserta movimientos nuevos (como BbvaBankStatementImporter/
    /// ImportFileProcessingSink) -- estampa el importBatchId recibido en cada Transaction,
    /// mismo punto y mismo criterio que la implementación real.
    /// </summary>
    private sealed class InsertingHandler(IServiceScopeFactory scopeFactory, int movementCount) : IFileImportHandler
    {
        public string HandlerName => "Inserting";

        public bool CanHandle(string filePath) => true;

        public async Task<ImportRunResult> HandleAsync(string filePath, Guid importBatchId, CancellationToken ct = default)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            for (var i = 0; i < movementCount; i++)
            {
                db.Transactions.Add(new Transaction
                {
                    Date = DateTime.UtcNow,
                    Description = $"movimiento-{i}",
                    Amount = 10m,
                    Currency = "ARS",
                    CreatedAtUtc = DateTime.UtcNow,
                    ExternalId = Guid.NewGuid().ToString(),
                    SourceFile = filePath,
                    ImportBatchId = importBatchId,
                });
            }

            await db.SaveChangesAsync(ct);
            return new ImportRunResult(movementCount, 0, 0, 0, []);
        }
    }

    /// <summary>
    /// Simula BbvaDebitCardEnrichmentHandler: recibe importBatchId por contrato pero
    /// nunca lo usa -- solo modifica un campo de un BankStatement ya existente
    /// (representa Merchant/MerchantAtUtc), sin insertar nada ni tocar ImportBatchId.
    /// </summary>
    private sealed class EnrichmentOnlyHandler(IServiceScopeFactory scopeFactory) : IFileImportHandler
    {
        public string HandlerName => "EnrichmentOnly";

        public bool CanHandle(string filePath) => true;

        public async Task<ImportRunResult> HandleAsync(string filePath, Guid unusedImportBatchId, CancellationToken ct = default)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            var statement = await db.BankStatements.SingleAsync(ct);
            statement.Concept = "ENRIQUECIDO";
            await db.SaveChangesAsync(ct);

            return new ImportRunResult(0, 0, 0, 0, []);
        }
    }

    /// <summary>
    /// AppDbContext real, salvo que SaveChangesAsync falla específicamente cuando hay un
    /// ImportBatch pendiente de insertar -- reproduce, sin tocar producción, el escenario
    /// "PersistImportBatchAsync falla después de que el handler ya guardó sus datos".
    /// Cualquier otro SaveChangesAsync (el del handler, que solo agrega Transaction) se
    /// comporta exactamente igual que el AppDbContext real.
    /// </summary>
    private sealed class FailOnImportBatchInsertDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var hasPendingImportBatch = ChangeTracker.Entries<ImportBatch>()
                .Any(e => e.State == EntityState.Added);

            if (hasPendingImportBatch)
                throw new InvalidOperationException("Fallo simulado al persistir ImportBatch.");

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
