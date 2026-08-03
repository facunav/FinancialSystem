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
/// Cubre el comportamiento de validación previa agregado a FileImportRouter en el
/// Patch 0051 (Epic P): un archivo rechazado por IImportFileValidator nunca llega a
/// ofrecerse a ningún IFileImportHandler, y tanto ese rechazo como el caso "ningún
/// handler reconoce el archivo" quedan registrados en ImportBatch igual que una corrida
/// real -- antes de este patch, "ningún handler aceptó" no dejaba ningún rastro
/// persistido. Usa el proveedor InMemory de EF Core, mismo patrón que
/// ImportFileProcessingSinkIdempotencyTests.
/// </summary>
public class FileImportRouterValidationTests
{
    [Fact]
    public async Task InvalidFile_NeverReachesAnyHandler_AndPersistsRejectionBatch()
    {
        var dbName = Guid.NewGuid().ToString();
        var handler = new SpyFileImportHandler(canHandle: true);
        var router = CreateRouter(
            dbName,
            [handler],
            new FakeImportFileValidator(ImportFileValidationResult.Invalid("Archivo vacío (0 bytes).")));

        await router.RouteAsync("vacio.csv");

        Assert.False(handler.WasInvoked);

        await using var db = OpenDb(dbName);
        var batch = await db.ImportBatches.SingleAsync();
        Assert.Equal("Validation", batch.HandlerName);
        Assert.Equal(0, batch.InsertedCount);
        Assert.Equal(1, batch.FailedCount);
    }

    [Fact]
    public async Task NoHandlerMatches_PersistsRejectionBatch()
    {
        var dbName = Guid.NewGuid().ToString();
        // canHandle=false: el archivo es válido para la validación previa, pero ningún
        // handler registrado lo reconoce (ej.: extensión sin parser asociado).
        var handler = new SpyFileImportHandler(canHandle: false);
        var router = CreateRouter(dbName, [handler], new FakeImportFileValidator(ImportFileValidationResult.Valid));

        await router.RouteAsync("desconocido.ofx");

        Assert.False(handler.WasInvoked);

        await using var db = OpenDb(dbName);
        var batch = await db.ImportBatches.SingleAsync();
        Assert.Equal("Validation", batch.HandlerName);
        Assert.Equal(0, batch.InsertedCount);
        Assert.Equal(1, batch.FailedCount);
    }

    [Fact]
    public async Task ValidFileWithMatchingHandler_InvokesHandlerAndPersistsItsResult()
    {
        // Estructura correcta aceptada: la validación previa no debe interponerse cuando
        // el archivo es válido y hay un handler dispuesto a procesarlo.
        var dbName = Guid.NewGuid().ToString();
        var handler = new SpyFileImportHandler(
            canHandle: true,
            result: new ImportRunResult(3, 0, 0, 0, []));
        var router = CreateRouter(dbName, [handler], new FakeImportFileValidator(ImportFileValidationResult.Valid));

        await router.RouteAsync("extracto_valido.xls");

        Assert.True(handler.WasInvoked);

        await using var db = OpenDb(dbName);
        var batch = await db.ImportBatches.SingleAsync();
        Assert.Equal(handler.HandlerName, batch.HandlerName);
        Assert.Equal(3, batch.InsertedCount);
        Assert.Equal(0, batch.FailedCount);
    }

    private static FileImportRouter CreateRouter(
        string dbName,
        IReadOnlyList<IFileImportHandler> handlers,
        IImportFileValidator validator)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new FileImportRouter(
            handlers,
            validator,
            new NoOpConsistencyVerifier(),
            new NoOpPipelineDiagnostics(),
            scopeFactory,
            new FakeDateTimeProvider(),
            NullLogger<FileImportRouter>.Instance);
    }

    private static AppDbContext OpenDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private sealed class FakeImportFileValidator(ImportFileValidationResult result) : IImportFileValidator
    {
        public ImportFileValidationResult Validate(string filePath) => result;
    }

    /// <summary>Registra si HandleAsync fue invocado -- prueba directa de "no se ejecuta ningún handler".</summary>
    private sealed class SpyFileImportHandler(bool canHandle, ImportRunResult? result = null) : IFileImportHandler
    {
        public bool WasInvoked { get; private set; }

        public string HandlerName => "Spy";

        public bool CanHandle(string filePath) => canHandle;

        public Task<ImportRunResult> HandleAsync(string filePath, CancellationToken ct = default)
        {
            WasInvoked = true;
            return Task.FromResult(result ?? new ImportRunResult(0, 0, 0, 0, []));
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
