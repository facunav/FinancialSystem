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
/// Cubre la consistencia transaccional agregada a FileImportRouter en el Patch 0053
/// (Epic P): ninguna excepción -- ocurra antes de cualquier persistencia, dentro de un
/// handler, o incluso después de que un handler haya empezado a agregar movimientos al
/// contexto -- debe dejar el historial de importaciones en un estado ambiguo ni debe
/// impedir que el mismo archivo se reintente exitosamente más adelante. Usa el proveedor
/// InMemory de EF Core, mismo patrón que FileImportRouterValidationTests (Patch 0051) y
/// FileImportRouterIdempotencyTests (Patch 0052).
/// </summary>
public class FileImportRouterConsistencyTests
{
    [Fact]
    public async Task ExceptionBeforeAnyPersistence_NeverInvokesAnyHandlerAndPersistsFailedBatch()
    {
        // "Excepción antes de persistir": el propio validador (antes de tocar handlers o
        // parsers) lanza una excepción no controlada -- ver ImportFileValidator, cuya
        // implementación real ya protege esto; esta es la red de seguridad del router
        // por si algo similar ocurriera en un paso previo a la persistencia.
        var dbName = Guid.NewGuid().ToString();
        var handler = new CountingFileImportHandler(insertedPerRun: 4);
        var router = CreateRouter(dbName, handler, new ThrowingValidator());

        await router.RouteAsync("cualquier_archivo.csv");

        Assert.Equal(0, handler.InvocationCount);

        await using var db = OpenDb(dbName);
        var batch = await db.ImportBatches.SingleAsync();
        Assert.Equal("Router", batch.HandlerName);
        Assert.Equal(0, batch.InsertedCount);
        Assert.Equal(1, batch.FailedCount);
        Assert.Empty(batch.ContentHash);
    }

    [Fact]
    public async Task ExceptionDuringHandler_ProducesFailedBatchWithZeroCounts()
    {
        // "Excepción durante la persistencia": el handler lanza mientras hace su trabajo
        // (representa cualquier falla real de un IFileImportHandler, sea al leer el
        // archivo, parsearlo o guardarlo).
        var path = CreateTempFile("contenido-cualquiera"u8.ToArray(), $"{Guid.NewGuid():N}.csv");
        var dbName = Guid.NewGuid().ToString();
        try
        {
            var handler = new ThrowingFileImportHandler();
            var router = CreateRouter(dbName, handler, new AlwaysValidValidator());

            await router.RouteAsync(path);

            Assert.Equal(1, handler.InvocationCount);

            await using var db = OpenDb(dbName);
            var batch = await db.ImportBatches.SingleAsync();
            Assert.Equal(handler.HandlerName, batch.HandlerName);
            Assert.Equal(0, batch.InsertedCount);
            Assert.Equal(0, batch.DuplicateCount);
            Assert.Equal(1, batch.FailedCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExceptionAfterAddingSomeMovements_RollsBackCompletely_NoOrphanedTransactions()
    {
        // "Excepción después de insertar algunos movimientos" + "rollback completo" +
        // "ausencia de registros huérfanos": el handler agrega movimientos al contexto
        // (como haría un handler real antes de su SaveChangesAsync final) y después
        // lanza, sin llegar a guardar. La garantía de EF Core (un único SaveChangesAsync
        // es atómico) hace que nada de eso quede persistido.
        var path = CreateTempFile("contenido-b"u8.ToArray(), $"{Guid.NewGuid():N}.csv");
        var dbName = Guid.NewGuid().ToString();
        try
        {
            var services = BuildServices(dbName);
            var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
            var handler = new PartiallyInsertingThenThrowingHandler(scopeFactory);
            var router = CreateRouter(scopeFactory, handler, new AlwaysValidValidator());

            await router.RouteAsync(path);

            await using var db = OpenDb(dbName);

            // Rollback completo: los dos Transaction que el handler agregó al change
            // tracker antes de lanzar nunca llegaron a un SaveChangesAsync -- cero filas.
            Assert.Equal(0, await db.Transactions.CountAsync());

            var batch = await db.ImportBatches.SingleAsync();
            Assert.Equal(0, batch.InsertedCount);
            Assert.Equal(1, batch.FailedCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AfterFailedImport_SameFileCanBeRetriedSuccessfully()
    {
        // "Posibilidad de reintentar la importación luego de un fallo": un intento
        // fallido no debe dejar residuos que bloqueen un reintento posterior del mismo
        // contenido -- ni por idempotencia (ContentHash vacío en la corrida Failed, ver
        // FileImportRouter.RouteAsync) ni por ningún otro estado intermedio.
        var path = CreateTempFile("contenido-reintentable"u8.ToArray(), $"{Guid.NewGuid():N}.csv");
        var dbName = Guid.NewGuid().ToString();
        try
        {
            var throwingHandler = new ThrowingFileImportHandler();
            var routerAttempt1 = CreateRouter(dbName, throwingHandler, new AlwaysValidValidator());
            await routerAttempt1.RouteAsync(path);

            // Mismo archivo, mismo contenido -- esta vez con un handler que sí completa
            // (representa que el problema que causó la falla ya no está presente).
            var succeedingHandler = new CountingFileImportHandler(insertedPerRun: 6);
            var routerAttempt2 = CreateRouter(dbName, succeedingHandler, new AlwaysValidValidator());
            await routerAttempt2.RouteAsync(path);

            // El reintento sí llegó a procesar -- no se saltea como si ya estuviera importado.
            Assert.Equal(1, succeedingHandler.InvocationCount);

            await using var db = OpenDb(dbName);
            var batches = await db.ImportBatches.ToListAsync();
            Assert.Equal(2, batches.Count);

            var failedBatch = batches.Single(b => b.HandlerName == throwingHandler.HandlerName);
            var successBatch = batches.Single(b => b.HandlerName == succeedingHandler.HandlerName);
            Assert.Equal(1, failedBatch.FailedCount);
            Assert.Equal(6, successBatch.InsertedCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task HistoryStaysConsistentAcrossRejectedFailedAndCompletedRuns()
    {
        // "Consistencia del historial": sin importar el desenlace (rechazado, fallido o
        // completado), cada RouteAsync deja exactamente un ImportBatch, nunca cero ni más
        // de uno, y cada fila refleja fielmente lo que pasó.
        var dbName = Guid.NewGuid().ToString();

        var rejectedRouter = CreateRouter(dbName, new CountingFileImportHandler(0), new InvalidValidator());
        await rejectedRouter.RouteAsync("rechazado.csv");

        var failedPath = CreateTempFile("contenido-fallido"u8.ToArray(), $"{Guid.NewGuid():N}_falla.csv");
        var failedHandler = new ThrowingFileImportHandler();
        var failedRouter = CreateRouter(dbName, failedHandler, new AlwaysValidValidator());

        var completedPath = CreateTempFile("contenido-completo"u8.ToArray(), $"{Guid.NewGuid():N}_ok.csv");
        var completedHandler = new CountingFileImportHandler(insertedPerRun: 9);
        var completedRouter = CreateRouter(dbName, completedHandler, new AlwaysValidValidator());
        try
        {
            await failedRouter.RouteAsync(failedPath);
            await completedRouter.RouteAsync(completedPath);

            await using var db = OpenDb(dbName);
            var batches = await db.ImportBatches.ToListAsync();
            Assert.Equal(3, batches.Count);

            var rejected = batches.Single(b => b.HandlerName == "Validation");
            var failed = batches.Single(b => b.HandlerName == failedHandler.HandlerName);
            var completed = batches.Single(b => b.HandlerName == completedHandler.HandlerName);

            Assert.Equal(1, rejected.FailedCount);
            Assert.Equal(0, rejected.InsertedCount);
            Assert.Equal(1, failed.FailedCount);
            Assert.Equal(0, failed.InsertedCount);
            Assert.Equal(0, completed.FailedCount);
            Assert.Equal(9, completed.InsertedCount);
        }
        finally
        {
            File.Delete(failedPath);
            File.Delete(completedPath);
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
        CreateRouter(BuildServices(dbName).GetRequiredService<IServiceScopeFactory>(), handler, validator);

    private static FileImportRouter CreateRouter(
        IServiceScopeFactory scopeFactory, IFileImportHandler handler, IImportFileValidator validator) =>
        new(
            [handler],
            validator,
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

    private sealed class InvalidValidator : IImportFileValidator
    {
        public ImportFileValidationResult Validate(string filePath) =>
            ImportFileValidationResult.Invalid("Archivo inválido de prueba.");
    }

    private sealed class ThrowingValidator : IImportFileValidator
    {
        public ImportFileValidationResult Validate(string filePath) =>
            throw new InvalidOperationException("Fallo simulado antes de cualquier persistencia.");
    }

    private sealed class CountingFileImportHandler(int insertedPerRun) : IFileImportHandler
    {
        public int InvocationCount { get; private set; }

        public string HandlerName => "Counting";

        public bool CanHandle(string filePath) => true;

        public Task<ImportRunResult> HandleAsync(string filePath, CancellationToken ct = default)
        {
            InvocationCount++;
            return Task.FromResult(new ImportRunResult(insertedPerRun, 0, 0, 0, []));
        }
    }

    private sealed class ThrowingFileImportHandler : IFileImportHandler
    {
        public int InvocationCount { get; private set; }

        public string HandlerName => "ThrowingHandler";

        public bool CanHandle(string filePath) => true;

        public Task<ImportRunResult> HandleAsync(string filePath, CancellationToken ct = default)
        {
            InvocationCount++;
            throw new InvalidOperationException("Fallo simulado dentro del handler.");
        }
    }

    /// <summary>
    /// Agrega movimientos al change tracker de su propio scope (igual que un handler
    /// real) y lanza ANTES de llamar SaveChangesAsync -- simula una falla a mitad de la
    /// persistencia para probar que nada de eso queda guardado.
    /// </summary>
    private sealed class PartiallyInsertingThenThrowingHandler(IServiceScopeFactory scopeFactory) : IFileImportHandler
    {
        public string HandlerName => "PartialInsertThenThrow";

        public bool CanHandle(string filePath) => true;

        public async Task<ImportRunResult> HandleAsync(string filePath, CancellationToken ct = default)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            db.Transactions.Add(new Transaction
            {
                Date = DateTime.UtcNow,
                Description = "movimiento-parcial-1",
                Amount = 10m,
                Currency = "ARS",
                CreatedAtUtc = DateTime.UtcNow,
                ExternalId = Guid.NewGuid().ToString()
            });
            db.Transactions.Add(new Transaction
            {
                Date = DateTime.UtcNow,
                Description = "movimiento-parcial-2",
                Amount = 20m,
                Currency = "ARS",
                CreatedAtUtc = DateTime.UtcNow,
                ExternalId = Guid.NewGuid().ToString()
            });

            throw new InvalidOperationException("Fallo simulado después de agregar movimientos, antes de SaveChangesAsync.");
        }
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
}
