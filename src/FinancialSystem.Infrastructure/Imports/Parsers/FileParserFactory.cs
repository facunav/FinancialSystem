using FinancialSystem.Application.Imports;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Imports.Parsers;

/// <summary>
/// Factory que resuelve el parser correcto para un archivo dado.
///
/// PROBLEMA QUE RESUELVE:
///   Cuando hay múltiples parsers para el mismo tipo de archivo (ej: BBVA Visa y BBVA
///   Mastercard son ambos .pdf), la selección por extensión es insuficiente.
///   Necesitamos selección por CONTENIDO.
///
/// ESTRATEGIA:
///   1. Por extensión: para CSV y Excel (un solo parser por extensión).
///   2. Por contenido: para PDF (múltiples parsers, cada uno implementa
///      IStatementParser.CanHandle() para auto-identificarse).
///
/// AMBIGÜEDAD (Patch 0050 — Integridad de Importación):
///   Si más de un parser reconoce el mismo archivo (por extensión o por contenido), NO se
///   elige ninguno por prioridad implícita de registro en DI. Se devuelve
///   FileParserResolutionStatus.Ambiguous con los parsers en conflicto — el caller
///   (ImportFileProcessingSink) debe abortar la importación de ese archivo. Antes de este
///   patch, un PDF que matcheara los fingerprints de BBVA Visa y BBVA Mastercard a la vez
///   se procesaba en silencio con el primero registrado, con riesgo real de datos
///   incorrectos o movimientos perdidos.
///
/// EXTENSIÓN:
///   Registrar cualquier IFileParser en el DI. Si implementa también
///   IStatementParser, participa en el routing por contenido de PDF.
///   No hay que tocar esta clase.
/// </summary>
internal sealed class FileParserFactory : IFileParserFactory
{
    private readonly IReadOnlyList<IFileParser> _allParsers;
    private readonly IReadOnlyList<IStatementParser> _pdfParsers;
    private readonly IPdfTextExtractor _textExtractor;
    private readonly ILogger<FileParserFactory> _logger;

    public FileParserFactory(
        IEnumerable<IFileParser> parsers,
        IPdfTextExtractor textExtractor,
        ILogger<FileParserFactory> logger)
    {
        _allParsers = parsers.ToList();
        // Los parsers PDF implementan AMBAS interfaces para participar en content routing
        _pdfParsers = _allParsers.OfType<IStatementParser>().ToList();
        _textExtractor = textExtractor;
        _logger = logger;

        _logger.LogInformation(
            "FileParserFactory: {Total} parsers registrados ({PdfCount} PDF con content routing: {PdfIds})",
            _allParsers.Count,
            _pdfParsers.Count,
            string.Join(", ", _pdfParsers.Select(p => p.ParserId)));
    }

    public FileParserResolution ResolveParser(string filePath)
    {
        var extension = Path.GetExtension(filePath);

        // Para PDF: routing por contenido (hay múltiples parsers posibles)
        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return ResolvePdfParser(filePath);

        // Para otros formatos: routing por extensión. Hoy hay un único parser por
        // extensión en producción (CSV, XLSX), pero se evalúan TODOS los que declaren esa
        // extensión, no solo el primero — mismo criterio de seguridad que el routing PDF.
        var matches = _allParsers
            .Where(p => p.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            _logger.LogWarning("No hay parser registrado para la extensión '{Extension}'", extension);
            return FileParserResolution.NotFound;
        }

        if (matches.Count > 1)
        {
            var conflictingIds = matches.Select(p => p.GetType().Name).ToList();
            _logger.LogError(
                "Múltiples parsers coinciden con la extensión '{Extension}' de {FilePath}: {Parsers}. " +
                "No se elige ninguno automáticamente.",
                extension, filePath, string.Join(", ", conflictingIds));
            return FileParserResolution.Ambiguous(conflictingIds);
        }

        return FileParserResolution.Resolved(matches[0]);
    }

    private FileParserResolution ResolvePdfParser(string filePath)
    {
        if (_pdfParsers.Count == 0)
        {
            _logger.LogWarning("No hay parsers PDF registrados");
            return FileParserResolution.NotFound;
        }

        IReadOnlyList<string> lines;
        try
        {
            // Extracción sincrónica — la factory se usa en contexto sincrónico.
            // Para archivos grandes, el extractor ya tiene paginación interna.
            lines = _textExtractor.ExtractLinesAsync(filePath).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo leer el PDF {FilePath} para routing", filePath);
            return FileParserResolution.NotFound;
        }

        // Se evalúan TODOS los parsers PDF, no el primero que matchea. Si más de uno
        // reconoce el mismo documento es una ambigüedad real (ver doc-comment de la
        // clase), no un empate a resolver por prioridad implícita de registro en DI.
        var candidates = _pdfParsers.Where(p => p.CanHandle(lines)).ToList();

        if (candidates.Count == 0)
        {
            _logger.LogWarning(
                "Ningún parser reconoció el PDF {FilePath}. " +
                "Parsers intentados: {Parsers}",
                Path.GetFileName(filePath),
                string.Join(", ", _pdfParsers.Select(p => p.ParserId)));

            return FileParserResolution.NotFound;
        }

        if (candidates.Count > 1)
        {
            var conflictingIds = candidates.Select(p => p.ParserId).ToList();
            _logger.LogError(
                "Múltiples parsers PDF reconocen {FilePath}: {Parsers}. " +
                "No se elige ninguno automáticamente — la importación de este archivo debe abortar.",
                Path.GetFileName(filePath), string.Join(", ", conflictingIds));

            return FileParserResolution.Ambiguous(conflictingIds);
        }

        var selected = candidates[0];
        _logger.LogInformation(
            "PDF {FilePath} → parser '{ParserId}'",
            Path.GetFileName(filePath),
            selected.ParserId);

        return FileParserResolution.Resolved((IFileParser)selected);
    }
}
