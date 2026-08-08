using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Imports;
using FinancialSystem.Infrastructure.Imports;
using FinancialSystem.Infrastructure.Imports.Normalization;
using FinancialSystem.Infrastructure.Imports.Parsers;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Imports;

/// <summary>
/// Cubre atomicidad ante archivos corruptos/estructuralmente inválidos (Patch 0051, Epic
/// P): cuando el parser real falla al intentar procesarlos (archivo corrupto, columnas
/// obligatorias faltantes), ImportFileProcessingSink no debe persistir ningún movimiento
/// parcial. Usa parsers reales (CsvFileParser, ExcelWorkbookParser) sobre archivos
/// temporales reales -- no dobles de prueba -- para no depender de una interpretación de
/// su comportamiento interno, que este patch no modifica.
/// </summary>
public class ImportFileProcessingSinkValidationTests
{
    [Fact]
    public async Task CorruptXlsxFile_FailsWithoutPersistingAnyTransaction()
    {
        // "Corrupto": extensión .xlsx pero el contenido no es un ZIP válido -- exactamente
        // lo que XlsxSanitizer.StripDataValidations encuentra al abrir el archivo.
        var path = CreateTempFile("esto no es un archivo xlsx válido"u8.ToArray(), ".xlsx");
        var dbName = Guid.NewGuid().ToString();
        try
        {
            var sink = CreateSink(dbName, new ExcelWorkbookParser(NullLogger<ExcelWorkbookParser>.Instance));

            var result = await sink.HandleFileAsync(path, Guid.NewGuid());

            Assert.Equal(0, result.Inserted);
            Assert.True(result.Failed > 0);

            await using var db = OpenDb(dbName);
            Assert.Equal(0, await db.Transactions.CountAsync());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CsvMissingRequiredColumns_FailsWithoutPersistingAnyTransaction()
    {
        // Encabezado sin ninguna columna reconocible como fecha/descripción/monto.
        var path = CreateTempFile("columna_a,columna_b,columna_c\nx,y,z\n"u8.ToArray(), ".csv");
        var dbName = Guid.NewGuid().ToString();
        try
        {
            var sink = CreateSink(dbName, new CsvFileParser(NullLogger<CsvFileParser>.Instance));

            var result = await sink.HandleFileAsync(path, Guid.NewGuid());

            Assert.Equal(0, result.Inserted);
            Assert.True(result.Failed > 0);

            await using var db = OpenDb(dbName);
            Assert.Equal(0, await db.Transactions.CountAsync());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTempFile(byte[] content, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, content);
        return path;
    }

    private static ImportFileProcessingSink CreateSink(string dbName, IFileParser parser)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new ImportFileProcessingSink(
            new FakeFileParserFactory(parser),
            new TransactionNormalizer(),
            scopeFactory,
            new FakeDateTimeProvider(),
            NullLogger<ImportFileProcessingSink>.Instance);
    }

    private static AppDbContext OpenDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private sealed class FakeFileParserFactory(IFileParser parser) : IFileParserFactory
    {
        public FileParserResolution ResolveParser(string filePath) =>
            FileParserResolution.Resolved(parser);
    }

    private sealed class FakeDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);
    }
}
