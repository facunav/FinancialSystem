using FinancialSystem.Application.Imports;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace FinancialSystem.Infrastructure.Imports;

/// <summary>
/// Extrae texto de PDFs usando PdfPig con estrategia de reconstrucción por palabras.
///
/// DECISIÓN DE DISEÑO:
/// ContentOrderTextExtractor agrupa palabras respetando el orden de lectura visual,
/// que es crítico para PDFs con columnas o tablas. Esto es mucho más confiable
/// que extraer por posición absoluta X/Y.
/// </summary>
public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    private readonly ILogger<PdfPigTextExtractor> _logger;

    public PdfPigTextExtractor(ILogger<PdfPigTextExtractor> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<string>> ExtractLinesAsync(string filePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PDF no encontrado: {filePath}");

        _logger.LogDebug("Extrayendo texto de {FilePath}", filePath);

        var lines = new List<string>();

        try
        {
            using var document = PdfDocument.Open(filePath);

            foreach (var page in document.GetPages())
            {
                ct.ThrowIfCancellationRequested();

                // ContentOrderTextExtractor reconstruye el texto en orden de lectura,
                // fusionando palabras de la misma línea visual aunque estén en bloques distintos.
                var pageText = ContentOrderTextExtractor.GetText(page, addDoubleNewline: false);

                if (string.IsNullOrWhiteSpace(pageText))
                {
                    _logger.LogWarning("Página {PageNum} sin texto extraíble (posiblemente escaneada)", page.Number);
                    continue;
                }

                var pageLines = pageText
                    .Split('\n', StringSplitOptions.None)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l));

                lines.AddRange(pageLines);

                _logger.LogDebug("Página {PageNum}: {LineCount} líneas extraídas", page.Number, lines.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error extrayendo texto de {FilePath}", filePath);
            throw;
        }

        _logger.LogInformation("Extracción completa: {TotalLines} líneas en {FilePath}", lines.Count, filePath);

        return Task.FromResult<IReadOnlyList<string>>(lines.AsReadOnly());
    }
}
