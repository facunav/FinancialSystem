using FinancialSystem.Infrastructure.Imports;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Imports;

/// <summary>
/// Cubre ImportFileValidator (Patch 0051 — Integridad de Importación): validación de
/// "forma" a nivel de archivo (existe, no está vacío, se puede leer), independiente del
/// tipo/extensión. La validación estructural específica de cada formato (columnas de
/// CSV/Excel, contenido de PDF) se cubre en ImportFileProcessingSinkValidationTests y en
/// ImportHandlerSelectionTests (Patch 0050) — no se duplica acá.
/// </summary>
public class ImportFileValidatorTests
{
    private readonly ImportFileValidator _validator = new();

    [Fact]
    public void NonexistentFile_IsInvalid()
    {
        var path = Path.Combine(Path.GetTempPath(), $"no-existe-{Guid.NewGuid():N}.csv");

        var result = _validator.Validate(path);

        Assert.False(result.IsValid);
        Assert.NotNull(result.RejectionReason);
    }

    [Fact]
    public void EmptyFile_IsInvalid()
    {
        var path = CreateTempFile([]);
        try
        {
            var result = _validator.Validate(path);

            Assert.False(result.IsValid);
            Assert.Contains("vacío", result.RejectionReason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EmptyPdfFile_IsInvalid()
    {
        // Mismo caso que EmptyFile_IsInvalid con extensión .pdf -- la validación es
        // genérica por diseño (no conoce PDF específicamente), pero el caso concreto que
        // pide cubrirse es justamente este: un PDF sin contenido rechazado antes de
        // llegar a PdfPigTextExtractor/FileParserFactory.
        var path = CreateTempFile([], extension: ".pdf");
        try
        {
            var result = _validator.Validate(path);

            Assert.False(result.IsValid);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DirectoryPath_IsInvalid()
    {
        // File.Exists devuelve false para un directorio -- se rechaza por la misma vía
        // que "no existe", sin necesidad de abrir nada. Cubre el caso "no se puede leer
        // como archivo" de forma portable entre sistemas operativos.
        var directory = Path.Combine(Path.GetTempPath(), $"dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var result = _validator.Validate(directory);

            Assert.False(result.IsValid);
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void NonEmptyReadableFile_IsValid()
    {
        var path = CreateTempFile("fecha,descripcion,monto\n01/01/2026,Test,100"u8.ToArray());
        try
        {
            var result = _validator.Validate(path);

            Assert.True(result.IsValid);
            Assert.Null(result.RejectionReason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTempFile(byte[] content, string extension = ".csv")
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, content);
        return path;
    }
}
