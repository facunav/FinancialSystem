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
/// Cubre la idempotencia por contenido agregada a FileImportRouter en el Patch 0052
/// (Epic P): una reimportación del mismo archivo (mismo contenido, sin importar el
/// nombre) se saltea sin invocar a ningún handler una segunda vez; un archivo con el
/// mismo nombre pero contenido distinto se procesa como corrida nueva. Usa archivos
/// temporales reales (para que ImportBatch.ContentHash se calcule de verdad) y el
/// proveedor InMemory de EF Core, mismo patrón que FileImportRouterValidationTests
/// (Patch 0051).
/// </summary>
public class FileImportRouterIdempotencyTests
{
    [Fact]
    public async Task FirstImport_InvokesHandlerAndPersistsProcessedBatch()
    {
        var path = CreateTempFile("contenido-a"u8.ToArray(), $"{Guid.NewGuid():N}_primero.csv");
        var dbName = Guid.NewGuid().ToString();
        try
        {
            var handler = new CountingFileImportHandler(insertedPerRun: 5);
            var router = CreateRouter(dbName, handler);

            await router.RouteAsync(path);

            Assert.Equal(1, handler.InvocationCount);

            await using var db = OpenDb(dbName);
            var batch = await db.ImportBatches.SingleAsync();
            Assert.Equal(handler.HandlerName, batch.HandlerName);
            Assert.Equal(5, batch.InsertedCount);
            Assert.NotEmpty(batch.ContentHash);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SecondImport_SameFile_IsSkippedAndDoesNotInvokeHandlerAgain()
    {
        var path = CreateTempFile("contenido-b"u8.ToArray(), $"{Guid.NewGuid():N}_repetido.csv");
        var dbName = Guid.NewGuid().ToString();
        try
        {
            var handler = new CountingFileImportHandler(insertedPerRun: 5);
            var router = CreateRouter(dbName, handler);

            await router.RouteAsync(path);
            await router.RouteAsync(path); // reimportación exacta del mismo archivo

            // Verificación de que no existen movimientos duplicados: el handler que los
            // insertaría solo corrió una vez -- la segunda corrida nunca llegó a él.
            Assert.Equal(1, handler.InvocationCount);

            await using var db = OpenDb(dbName);
            var batches = await db.ImportBatches.ToListAsync();

            // Comportamiento consistente del historial: cada RouteAsync deja su propia
            // fila, incluso cuando se saltea -- el historial no queda "mudo" ni pierde la
            // segunda corrida. Se identifica cada fila por HandlerName (no por orden/fecha,
            // que con un reloj de prueba fijo pueden empatar entre ambas corridas).
            Assert.Equal(2, batches.Count);
            var processedBatch = batches.Single(b => b.HandlerName == handler.HandlerName);
            var skippedBatch = batches.Single(b => b.HandlerName == "Idempotency");
            Assert.Equal(5, processedBatch.InsertedCount);
            Assert.Equal(0, skippedBatch.InsertedCount);
            Assert.Equal(0, skippedBatch.FailedCount); // no es un error, es un salteo deliberado
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SameFileNameDifferentContent_IsProcessedAsNewImport()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_mismo_nombre.csv");
        var dbName = Guid.NewGuid().ToString();
        try
        {
            var handler = new CountingFileImportHandler(insertedPerRun: 2);
            var router = CreateRouter(dbName, handler);

            File.WriteAllBytes(path, "version-1-del-archivo"u8.ToArray());
            await router.RouteAsync(path);

            File.WriteAllBytes(path, "version-2-completamente-distinta"u8.ToArray());
            await router.RouteAsync(path);

            // La decisión se basa en el contenido, no en el nombre: mismo path, contenido
            // distinto -> ambas corridas procesan de verdad.
            Assert.Equal(2, handler.InvocationCount);

            await using var db = OpenDb(dbName);
            var batches = await db.ImportBatches.ToListAsync();
            Assert.Equal(2, batches.Count);
            Assert.All(batches, b => Assert.Equal(handler.HandlerName, b.HandlerName));
            Assert.NotEqual(batches[0].ContentHash, batches[1].ContentHash);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SameContentDifferentFileName_IsDetectedAsAlreadyImported()
    {
        var dbName = Guid.NewGuid().ToString();
        var testId = Guid.NewGuid().ToString("N");
        var pathA = CreateTempFile("contenido-identico-en-ambos-archivos"u8.ToArray(), $"{testId}_original.csv");
        var pathB = CreateTempFile("contenido-identico-en-ambos-archivos"u8.ToArray(), $"{testId}_renombrado_del_banco.csv");
        try
        {
            var handler = new CountingFileImportHandler(insertedPerRun: 3);
            var router = CreateRouter(dbName, handler);

            await router.RouteAsync(pathA);
            await router.RouteAsync(pathB); // nombre distinto, mismo contenido

            // La detección no depende únicamente del nombre del archivo.
            Assert.Equal(1, handler.InvocationCount);

            await using var db = OpenDb(dbName);
            var batches = await db.ImportBatches.ToListAsync();
            Assert.Equal(2, batches.Count);
            var processedBatch = batches.Single(b => b.HandlerName == handler.HandlerName);
            var skippedBatch = batches.Single(b => b.HandlerName == "Idempotency");
            Assert.Equal(0, skippedBatch.InsertedCount);
            Assert.Equal(processedBatch.ContentHash, skippedBatch.ContentHash);
        }
        finally
        {
            File.Delete(pathA);
            File.Delete(pathB);
        }
    }

    private static string CreateTempFile(byte[] content, string fileName)
    {
        var path = Path.Combine(Path.GetTempPath(), fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static FileImportRouter CreateRouter(string dbName, IFileImportHandler handler)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new FileImportRouter(
            [handler],
            new AlwaysValidValidator(),
            scopeFactory,
            new FakeDateTimeProvider(),
            NullLogger<FileImportRouter>.Instance);
    }

    private static AppDbContext OpenDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private sealed class AlwaysValidValidator : IImportFileValidator
    {
        public ImportFileValidationResult Validate(string filePath) => ImportFileValidationResult.Valid;
    }

    /// <summary>Handler que siempre acepta el archivo y cuenta cuántas veces se lo invocó.</summary>
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

    private sealed class FakeDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);
    }
}
