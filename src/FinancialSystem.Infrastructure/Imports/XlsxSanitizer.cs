using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Imports;

/// <summary>
/// Sanitiza un archivo .xlsx antes de pasárselo a ClosedXML.
///
/// PROBLEMA QUE RESUELVE:
///   ClosedXML 0.104.x lanza ArgumentOutOfRangeException al cargar archivos
///   que contienen DataValidations con fórmulas de más de 255 caracteres.
///   Esto ocurre cuando Excel usa concatenación de strings ("parte1"&"parte2")
///   para superar el límite de la UI — ClosedXML los resuelve y luego
///   valida el string resultante contra el límite, tirando la excepción.
///
/// CAUSA RAÍZ CONFIRMADA:
///   sheet2.xml (Gastos Fijos): DataValidation de categorías con 312 chars.
///   sheet4.xml (Dashboard):    DataValidation de categorías con 295 chars.
///   Ambas son listas desplegables (type="list") — no afectan los datos.
///
/// ESTRATEGIA:
///   Leer el xlsx como ZIP, eliminar los nodos &lt;dataValidations&gt; del XML
///   de cada hoja, y devolver el resultado como MemoryStream limpio.
///   ClosedXML recibe el stream ya saneado y nunca ve las validaciones.
///
/// POR QUÉ NO IGNORAR LA EXCEPCIÓN CON TRY/CATCH:
///   El crash ocurre dentro del constructor de XLWorkbook — en ese punto
///   el objeto está parcialmente inicializado y no es seguro usarlo.
///   La única solución correcta es prevenir el problema antes de abrir.
///
/// POR QUÉ NO ACTUALIZAR CLOSEDXML:
///   El bug está presente en la rama 0.x. La versión 0.104.2 es la última
///   estable de esa rama. Versión 1.x (en desarrollo) no es production-ready.
///   Esta solución es independiente de la versión de ClosedXML.
/// </summary>
public static class XlsxSanitizer
{
    // Namespace estándar de SpreadsheetML — fijo en todos los xlsx
    private const string SpreadsheetMlNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // Regex para identificar hojas de cálculo dentro del zip
    private static readonly Regex WorksheetPattern =
        new(@"^xl/worksheets/sheet\d+\.xml$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Lee el archivo xlsx, elimina todos los nodos dataValidations de cada
    /// hoja y devuelve el resultado como un MemoryStream listo para ClosedXML.
    ///
    /// No modifica el archivo original en disco.
    /// </summary>
    public static MemoryStream StripDataValidations(string filePath, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Archivo xlsx no encontrado: {filePath}");

        var outputStream = new MemoryStream();
        var totalStripped = 0;

        // Leer el xlsx original y escribir una copia saneada en memoria
        using (var inputStream = File.OpenRead(filePath))
        using (var inputZip  = new ZipArchive(inputStream, ZipArchiveMode.Read, leaveOpen: true))
        using (var outputZip = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in inputZip.Entries)
            {
                var outputEntry = outputZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);

                // Copiar timestamps para no alterar metadata
                outputEntry.LastWriteTime = entry.LastWriteTime;

                using var inputEntryStream  = entry.Open();
                using var outputEntryStream = outputEntry.Open();

                if (WorksheetPattern.IsMatch(entry.FullName))
                {
                    // Esta es una hoja — procesar y eliminar dataValidations
                    var stripped = StripDataValidationsFromSheet(
                        inputEntryStream, entry.FullName, logger, out var count);
                    totalStripped += count;
                    stripped.CopyTo(outputEntryStream);
                }
                else
                {
                    // Cualquier otro archivo del zip (shared strings, styles, etc.)
                    // se copia sin modificar
                    inputEntryStream.CopyTo(outputEntryStream);
                }
            }
        }

        if (totalStripped > 0)
        {
            logger?.LogDebug(
                "XlsxSanitizer: eliminadas {Count} validaciones de datos de {File}",
                totalStripped, Path.GetFileName(filePath));
        }

        outputStream.Position = 0;
        return outputStream;
    }

    private static Stream StripDataValidationsFromSheet(
        Stream sheetXml,
        string entryName,
        ILogger? logger,
        out int strippedCount)
    {
        strippedCount = 0;

        XDocument doc;
        try
        {
            doc = XDocument.Load(sheetXml);
        }
        catch (Exception ex)
        {
            // Si el XML de la hoja está corrupto, loguear y devolver vacío
            // Es preferible a crashear el proceso entero
            logger?.LogWarning(ex,
                "XlsxSanitizer: no se pudo parsear XML de {Entry}, se copia sin modificar",
                entryName);
            sheetXml.Position = 0;
            return sheetXml;
        }

        XNamespace ns = SpreadsheetMlNs;
        var dvNodes = doc.Root?
            .Elements(ns + "dataValidations")
            .ToList() ?? [];

        if (dvNodes.Count == 0)
        {
            // Sin validaciones — serializar el doc parseado (puede haber
            // normalizado whitespace, etc.) o simplemente devolver el original.
            // Para máxima fidelidad devolvemos el XML re-serializado.
            var noChangeStream = new MemoryStream();
            doc.Save(noChangeStream);
            noChangeStream.Position = 0;
            return noChangeStream;
        }

        foreach (var node in dvNodes)
        {
            strippedCount += node.Elements(ns + "dataValidation").Count();
            node.Remove();
        }

        var resultStream = new MemoryStream();
        doc.Save(resultStream);
        resultStream.Position = 0;
        return resultStream;
    }
}
