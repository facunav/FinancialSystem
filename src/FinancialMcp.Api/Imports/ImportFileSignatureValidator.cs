namespace FinancialSystem.Api.Imports;

/// <summary>
/// Validación de "magic bytes" previa al procesamiento (Patch 0063, PATCH-014, sección
/// 2) -- no reemplaza ni duplica a IFileParserFactory/los parsers (fuera de alcance de
/// este patch), solo evita que un archivo con extensión falsa o contenido corrupto
/// llegue a arrancar el pipeline de importación. La extensión ya se valida por
/// separado (FileIngestionOptions.WatchedExtensions) -- esto es una segunda señal
/// independiente para no confiar únicamente en el nombre del archivo.
///
/// .csv no tiene una firma binaria estándar (es texto plano) -- para esa extensión se
/// aplica una heurística (ausencia de bytes nulos en el encabezado) en vez de una
/// firma exacta, documentado explícitamente para no sugerir que valida el contenido
/// CSV en sí (eso lo sigue haciendo el parser correspondiente, sin cambios).
///
/// Pública (no internal): FinancialMcp.Api.Tests es un assembly separado y necesita
/// llamar a HasExpectedSignature directamente en sus tests unitarios -- a diferencia
/// de los servicios internos de Infrastructure (con interfaz pública propia, cubiertos
/// con fakes en los tests), esta clase no tiene una interfaz separada de la que
/// depender.
/// </summary>
public static class ImportFileSignatureValidator
{
    private static readonly byte[] PdfSignature = "%PDF"u8.ToArray();

    // .xlsx (Open Packaging Conventions) es un contenedor zip -- "PK" cubre las tres
    // variantes válidas de encabezado zip (local file header, empty archive, spanned
    // archive) sin sobre-ajustar a una sola.
    private static readonly byte[] ZipSignature = [0x50, 0x4B];

    // .xls (formato binario legacy de Excel) es un archivo OLE2/Compound File Binary.
    private static readonly byte[] OleSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    public static bool HasExpectedSignature(string extension, ReadOnlySpan<byte> header) =>
        extension.ToLowerInvariant() switch
        {
            ".pdf" => header.StartsWith(PdfSignature),
            ".xlsx" => header.StartsWith(ZipSignature),
            ".xls" => header.StartsWith(OleSignature),
            ".csv" => !header.Contains((byte)0),
            _ => true,
        };
}
