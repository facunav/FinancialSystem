using FinancialSystem.Api.Imports;
using Xunit;

namespace FinancialMcp.Api.Tests.Imports;

/// <summary>
/// Cubre ImportFileSignatureValidator (Patch 0063, PATCH-014, sección 2) de forma
/// aislada, sin HTTP -- ver ImportUploadValidationTests para la cobertura end-to-end
/// del endpoint (que reutiliza esta misma clase).
/// </summary>
public class ImportFileSignatureValidatorTests
{
    [Fact]
    public void HasExpectedSignature_Pdf_WithPdfHeader_ReturnsTrue()
    {
        byte[] header = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34]; // "%PDF-1.4"

        Assert.True(ImportFileSignatureValidator.HasExpectedSignature(".pdf", header));
    }

    [Fact]
    public void HasExpectedSignature_Pdf_WithNonPdfHeader_ReturnsFalse()
    {
        byte[] header = "not a pdf"u8.ToArray();

        Assert.False(ImportFileSignatureValidator.HasExpectedSignature(".pdf", header));
    }

    [Fact]
    public void HasExpectedSignature_Xlsx_WithZipHeader_ReturnsTrue()
    {
        byte[] header = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00];

        Assert.True(ImportFileSignatureValidator.HasExpectedSignature(".xlsx", header));
    }

    [Fact]
    public void HasExpectedSignature_Xlsx_WithNonZipHeader_ReturnsFalse()
    {
        byte[] header = [0x25, 0x50, 0x44, 0x46, 0x00, 0x00, 0x00, 0x00]; // header de PDF, no de zip

        Assert.False(ImportFileSignatureValidator.HasExpectedSignature(".xlsx", header));
    }

    [Fact]
    public void HasExpectedSignature_Xls_WithOleHeader_ReturnsTrue()
    {
        byte[] header = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

        Assert.True(ImportFileSignatureValidator.HasExpectedSignature(".xls", header));
    }

    [Fact]
    public void HasExpectedSignature_Xls_WithNonOleHeader_ReturnsFalse()
    {
        byte[] header = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00]; // header de zip/xlsx, no de xls

        Assert.False(ImportFileSignatureValidator.HasExpectedSignature(".xls", header));
    }

    [Fact]
    public void HasExpectedSignature_Csv_WithTextHeader_ReturnsTrue()
    {
        byte[] header = "fecha,monto"u8.ToArray();

        Assert.True(ImportFileSignatureValidator.HasExpectedSignature(".csv", header));
    }

    [Fact]
    public void HasExpectedSignature_Csv_WithNullByteInHeader_ReturnsFalse()
    {
        // Heurística: un byte nulo en el encabezado es señal de contenido binario
        // disfrazado de .csv -- ver el comentario de la clase sobre por qué .csv no
        // tiene una firma binaria exacta como las otras tres extensiones.
        byte[] header = [0x66, 0x00, 0x62, 0x61, 0x72, 0x00, 0x00, 0x00];

        Assert.False(ImportFileSignatureValidator.HasExpectedSignature(".csv", header));
    }

    [Fact]
    public void HasExpectedSignature_WithShorterHeaderThanSignature_ReturnsFalse()
    {
        // Archivo de 2 bytes declarado como .xls (firma de 8 bytes) -- no debe explotar
        // ni dar falso positivo, solo no coincidir.
        byte[] header = [0xD0, 0xCF];

        Assert.False(ImportFileSignatureValidator.HasExpectedSignature(".xls", header));
    }

    [Fact]
    public void HasExpectedSignature_IsCaseInsensitiveOnExtension()
    {
        byte[] header = "%PDF-1.4"u8.ToArray();

        Assert.True(ImportFileSignatureValidator.HasExpectedSignature(".PDF", header));
    }
}
