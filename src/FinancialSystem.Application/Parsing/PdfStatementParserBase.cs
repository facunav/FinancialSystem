using System.Diagnostics;
using System.Text.RegularExpressions;
using FinancialSystem.Application.Imports;
using FinancialSystem.Application.Imports.Parsing;
using FinancialSystem.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Application.Parsing;

/// <summary>
/// Clase base para todos los parsers de extractos PDF bancarios.
///
/// RESPONSABILIDADES DE ESTA CLASE:
///   - Orquesta la extracción de texto via IPdfTextExtractor
///   - Implementa la máquina de estados (Scanning → InSection → Done)
///   - Convierte Transaction[] → FileParseResult (contrato IFileParser)
///   - Logging estructurado consistente entre bancos
///
/// RESPONSABILIDADES DE LAS SUBCLASES:
///   - Definir fingerprints de identificación del banco
///   - Definir marcadores de inicio/fin de sección de consumos
///   - Implementar IsTransactionLine() y ParseLine()
///   - Definir ParserId (ej: "BBVA_VISA_AR", "GALICIA_MC_AR")
///
/// EXTENSIÓN:
///   Para agregar un banco nuevo, crear una subclase e implementar
///   los 3 métodos abstractos. No tocar esta clase ni la factory.
/// </summary>
public abstract class PdfStatementParserBase : IStatementParser, IFileParser
{
    private readonly IPdfTextExtractor _textExtractor;
    private readonly ILogger _logger;

    protected PdfStatementParserBase(IPdfTextExtractor textExtractor, ILogger logger)
    {
        _textExtractor = textExtractor;
        _logger = logger;
    }

    // ── Contrato IStatementParser ─────────────────────────────────

    public abstract string ParserId { get; }

    /// <summary>
    /// Determina si este parser puede manejar el documento.
    /// Implementación por defecto: busca fingerprints en las primeras N líneas.
    /// Las subclases pueden sobreescribir para lógica más compleja.
    /// </summary>
    public virtual bool CanHandle(IReadOnlyList<string> documentLines)
    {
        var headerLines = documentLines.Take(FingerprintScanLines);
        return headerLines.Any(line => Fingerprints.Any(fp => fp.IsMatch(line)));
    }

    public Task<IReadOnlyList<Transaction>> ParseAsync(
        IReadOnlyList<string> documentLines,
        string sourceFile,
        CancellationToken ct = default)
    {
        var transactions = new List<Transaction>();
        var state = SectionState.Scanning;
        var lineNumber = 0;
        var skippedLines = 0;
        var failedLines = new List<(int Line, string Text, string Reason)>();

        foreach (var line in documentLines)
        {
            ct.ThrowIfCancellationRequested();
            lineNumber++;

            switch (state)
            {
                case SectionState.Scanning:
                    if (IsSectionStart(line))
                    {
                        _logger.LogDebug("[{Parser}] Sección detectada en L{Line}: '{Line}'",
                            ParserId, lineNumber, line);
                        state = SectionState.InSection;
                    }
                    break;

                case SectionState.InSection:
                    if (IsSectionEnd(line))
                    {
                        _logger.LogDebug("[{Parser}] Fin de sección en L{Line}: '{Line}'",
                            ParserId, lineNumber, line);
                        // Algunos PDFs tienen MÚLTIPLES secciones (titular + adicionales).
                        // Volver a Scanning en lugar de Done para capturarlas todas.
                        state = SectionState.Scanning;
                        break;
                    }

                    if (!IsTransactionLine(line))
                    {
                        skippedLines++;
                        _logger.LogTrace("[{Parser}] L{Line} ignorada: '{Line}'",
                            ParserId, lineNumber, line);
                        break;
                    }

                    var result = ParseLine(line);
                    if (result.Success && result.Value is not null)
                    {
                        result.Value.SourceFile = sourceFile;
                        transactions.Add(result.Value);
                        _logger.LogDebug(
                            "[{Parser}] L{Line}: {Date} | {Desc} | {Currency} {Amount}",
                            ParserId, lineNumber,
                            result.Value.Date.ToString("dd-MMM-yy"),
                            result.Value.Description,
                            result.Value.Currency,
                            result.Value.Amount);
                    }
                    else
                    {
                        failedLines.Add((lineNumber, line, result.Error ?? "Unknown"));
                        _logger.LogWarning("[{Parser}] Error L{Line}: '{Line}' → {Error}",
                            ParserId, lineNumber, line, result.Error);
                    }
                    break;
            }
        }

        // Resumen de observabilidad
        _logger.LogInformation(
            "[{Parser}] {Source}: {Tx} transacciones | {Skipped} ignoradas | {Failed} fallos",
            ParserId, Path.GetFileName(sourceFile),
            transactions.Count, skippedLines, failedLines.Count);

        if (failedLines.Count > 0)
        {
            _logger.LogWarning("[{Parser}] Líneas fallidas:\n{Lines}",
                ParserId,
                string.Join("\n", failedLines.Select(f => $"  L{f.Line}: {f.Text} → {f.Reason}")));
        }

        if (state == SectionState.Scanning && transactions.Count == 0)
        {
            _logger.LogError(
                "[{Parser}] NO se encontró sección de consumos en {Source}. " +
                "Verificar marcadores o fingerprints.",
                ParserId, sourceFile);
        }

        return Task.FromResult<IReadOnlyList<Transaction>>(transactions.AsReadOnly());
    }

    // ── Contrato IFileParser ──────────────────────────────────────

    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".pdf"];

    public async Task<FileParseResult> ParseAsync(string filePath, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        IReadOnlyList<string> lines;
        try
        {
            lines = await _textExtractor.ExtractLinesAsync(filePath, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Parser}] Error extrayendo texto de {FilePath}", ParserId, filePath);
            return new FileParseResult([], 0, [$"Extracción fallida: {ex.Message}"], sw.Elapsed);
        }

        if (!CanHandle(lines))
        {
            // Este parser no es el correcto para este PDF — la factory lo manejará.
            return new FileParseResult([], 0,
                [$"[{ParserId}] PDF no reconocido por este parser."],
                sw.Elapsed);
        }

        var transactions = await ParseAsync(lines, filePath, ct);
        var extracted = transactions
            .Select(t => new ExtractedTransaction(
                t.Date,
                t.Description,
                t.Amount,
                t.Currency,
                t.CouponNumber,
                t.RawLine,
                t.SourceFile))
            .ToList();

        sw.Stop();
        return new FileParseResult(extracted, 0, [], sw.Elapsed);
    }

    // ── API para subclases ────────────────────────────────────────

    /// <summary>Número de líneas iniciales donde buscar fingerprints.</summary>
    protected virtual int FingerprintScanLines => 40;

    /// <summary>Patrones que identifican que el documento pertenece a este banco/tarjeta.</summary>
    protected abstract IReadOnlyList<Regex> Fingerprints { get; }

    /// <summary>Patrones de inicio de la sección de consumos.</summary>
    protected abstract IReadOnlyList<Regex> SectionStartPatterns { get; }

    /// <summary>Patrones de fin de la sección de consumos.</summary>
    protected abstract IReadOnlyList<Regex> SectionEndPatterns { get; }

    /// <summary>
    /// Determina si la línea es una transacción parseable.
    /// Debe ser rápido: sólo validaciones básicas, sin parseo completo.
    /// </summary>
    protected abstract bool IsTransactionLine(string line);

    /// <summary>
    /// Parsea una línea de transacción. Nunca lanza excepciones por datos malformados.
    /// </summary>
    protected abstract ParseResult<Transaction> ParseLine(string line);

    // ── Helpers privados ──────────────────────────────────────────

    private bool IsSectionStart(string line) =>
        SectionStartPatterns.Any(p => p.IsMatch(line));

    private bool IsSectionEnd(string line) =>
        SectionEndPatterns.Any(p => p.IsMatch(line));

    private enum SectionState { Scanning, InSection }
}
