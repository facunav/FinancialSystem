using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Imports;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Infrastructure.Imports;
using FinancialSystem.Infrastructure.Imports.Normalization;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Imports;

/// <summary>
/// Cubre la idempotencia de ImportFileProcessingSink entre corridas (Épica I3).
/// Antes de este fix, reimportar un archivo cuyas transacciones ya existían chocaba
/// contra el índice único de Transactions.ExternalId sin manejo — y como
/// SaveChangesAsync es una sola transacción implícita, también se perdían las
/// transacciones nuevas del mismo archivo. Usa el proveedor InMemory de EF Core (una
/// base por test, vía Guid) para ejercitar la consulta batch real contra IApplicationDbContext,
/// no un doble de prueba que simule el comportamiento.
/// </summary>
public class ImportFileProcessingSinkIdempotencyTests
{
    [Fact]
    public async Task HandleFileAsync_ReimportarElMismoArchivo_NoLanzaYNoDuplica()
    {
        var dbName = Guid.NewGuid().ToString();
        var transaccion = new ExtractedTransaction(
            new DateTime(2026, 3, 28), "DLO*PEDIDOSYA MCDONALD", 32725.00m, "ARS", "003842");

        var primeraCorrida = CreateSink(dbName, [transaccion]);
        var primerResultado = await primeraCorrida.HandleFileAsync("resumen.pdf");

        Assert.Equal(1, primerResultado.Inserted);
        Assert.Equal(0, primerResultado.Duplicates);

        // Segunda corrida: mismo archivo, mismas transacciones -- ExternalId ya existente.
        var segundaCorrida = CreateSink(dbName, [transaccion]);
        var segundoResultado = await segundaCorrida.HandleFileAsync("resumen.pdf");

        Assert.Equal(0, segundoResultado.Inserted);
        Assert.Equal(1, segundoResultado.Duplicates);

        await using var db = OpenDb(dbName);
        Assert.Equal(1, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task HandleFileAsync_ArchivoParcialmenteRepetido_InsertaSoloLasTransaccionesNuevas()
    {
        var dbName = Guid.NewGuid().ToString();
        var yaImportada = new ExtractedTransaction(
            new DateTime(2026, 3, 28), "DLO*PEDIDOSYA MCDONALD", 32725.00m, "ARS", "003842");
        var nueva = new ExtractedTransaction(
            new DateTime(2026, 3, 29), "FARMACITY", 5000.00m, "ARS", "003843");

        var primeraCorrida = CreateSink(dbName, [yaImportada]);
        await primeraCorrida.HandleFileAsync("resumen.pdf");

        // Segunda corrida: el resumen actualizado trae la operación ya importada
        // más una nueva -- ambas en el mismo archivo.
        var segundaCorrida = CreateSink(dbName, [yaImportada, nueva]);
        var resultado = await segundaCorrida.HandleFileAsync("resumen_actualizado.pdf");

        Assert.Equal(1, resultado.Inserted);
        Assert.Equal(1, resultado.Duplicates);

        await using var db = OpenDb(dbName);
        Assert.Equal(2, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task HandleFileAsync_ConCuentaFinancieraCoincidente_AsignaFinancialAccountId()
    {
        var dbName = Guid.NewGuid().ToString();
        var financialAccountId = await SeedFinancialAccountAsync(dbName, "1278896210");

        // Número de cuenta extraído del encabezado del PDF ("Visa Signature cuenta
        // 1278896210 CONSOLIDADO"), igual para todas las transacciones del archivo.
        var transaccion = new ExtractedTransaction(
            new DateTime(2026, 6, 15), "PLAYSTATION USD 4,99", 4.99m, "USD", "886221",
            null, null, "1278896210");

        var sink = CreateSink(dbName, [transaccion]);
        await sink.HandleFileAsync("Visa_Junio.pdf");

        await using var db = OpenDb(dbName);
        var transaction = await db.Transactions.SingleAsync();
        Assert.Equal(financialAccountId, transaction.FinancialAccountId);
    }

    [Fact]
    public async Task HandleFileAsync_SinCuentaFinancieraCoincidente_FinancialAccountIdQuedaNull()
    {
        var dbName = Guid.NewGuid().ToString();
        // Ninguna FinancialAccount sembrada -- comportamiento actual preservado: sin
        // match, la transacción queda sin cuenta asignada, igual que hoy.
        var transaccion = new ExtractedTransaction(
            new DateTime(2026, 6, 15), "PLAYSTATION USD 4,99", 4.99m, "USD", "886221",
            null, null, "1278896210");

        var sink = CreateSink(dbName, [transaccion]);
        await sink.HandleFileAsync("Visa_Junio.pdf");

        await using var db = OpenDb(dbName);
        var transaction = await db.Transactions.SingleAsync();
        Assert.Null(transaction.FinancialAccountId);
    }

    private static async Task<Guid> SeedFinancialAccountAsync(string dbName, string accountNumber)
    {
        await using var db = OpenDb(dbName);
        var account = new FinancialAccount
        {
            Name = "Visa BBVA",
            Type = FinancialAccountType.Card,
            AccountNumber = accountNumber,
            IsDeactivated = false,
        };
        db.FinancialAccounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private static ImportFileProcessingSink CreateSink(
        string dbName, IReadOnlyList<ExtractedTransaction> transactions)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new ImportFileProcessingSink(
            new FakeFileParserFactory(new FakeFileParser(transactions)),
            new TransactionNormalizer(),
            scopeFactory,
            new FakeDateTimeProvider(),
            NullLogger<ImportFileProcessingSink>.Instance);
    }

    private static AppDbContext OpenDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private sealed class FakeFileParserFactory(IFileParser parser) : IFileParserFactory
    {
        public bool TryGetParser(string filePath, out IFileParser? resolvedParser)
        {
            resolvedParser = parser;
            return true;
        }
    }

    private sealed class FakeFileParser(IReadOnlyList<ExtractedTransaction> transactions) : IFileParser
    {
        public IReadOnlyCollection<string> SupportedExtensions => [".pdf"];

        public Task<FileParseResult> ParseAsync(string filePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileParseResult(transactions, 0, [], TimeSpan.Zero));
    }

    private sealed class FakeDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);
    }
}
