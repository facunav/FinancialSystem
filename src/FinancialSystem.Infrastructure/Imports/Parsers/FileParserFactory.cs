using FinancialSystem.Application.Imports;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Imports.Parsers;

/// <summary>
/// Factory que resuelve el parser correcto para un archivo dado.
///
/// PROBLEMA QUE RESUELVE:
///   Cuando hay múltiples parsers para el mismo tipo de archivo (ej: BBVA y
///   Mastercard son ambos .pdf), la selección por extensión es insuficiente.
///   Necesitamos selección por CONTENIDO.
///
/// ESTRATEGIA:
///   1. Por extensión: para CSV y Excel (un solo parser por extensión).
///   2. Por contenido: para PDF (múltiples parsers, cada uno implementa
///      IStatementParser.CanHandle() para auto-identificarse).
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

    public bool TryGetParser(string filePath, out IFileParser? parser)
    {
        var extension = Path.GetExtension(filePath);

        // Para PDF: routing por contenido (hay múltiples parsers posibles)
        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            parser = ResolvePdfParser(filePath);
            return parser is not null;
        }

        // Para otros formatos: routing por extensión (un parser por extensión)
        parser = _allParsers.FirstOrDefault(p =>
            p.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase));

        if (parser is null)
            _logger.LogWarning("No hay parser registrado para la extensión '{Extension}'", extension);

        return parser is not null;
    }

    private IFileParser? ResolvePdfParser(string filePath)
    {
        if (_pdfParsers.Count == 0)
        {
            _logger.LogWarning("No hay parsers PDF registrados");
            return null;
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
            return null;
        }

        // Intentamos cada parser en orden de registro.
        // El orden en DI determina la prioridad si dos parsers hacen CanHandle=true.
        foreach (var statementParser in _pdfParsers)
        {
            if (statementParser.CanHandle(lines))
            {
                _logger.LogInformation(
                    "PDF {FilePath} → parser '{ParserId}'",
                    Path.GetFileName(filePath),
                    statementParser.ParserId);
                return (IFileParser)statementParser;
            }
        }

        _logger.LogWarning(
            "Ningún parser reconoció el PDF {FilePath}. " +
            "Parsers intentados: {Parsers}",
            Path.GetFileName(filePath),
            string.Join(", ", _pdfParsers.Select(p => p.ParserId)));

        return null;
    }
}
