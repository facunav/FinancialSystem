using FinancialSystem.Application.Imports;
using FinancialSystem.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FinancialSystem.Application.Parsing.Bbva;

/// <summary>
/// Parser completo para extractos BBVA Visa Argentina.
///
/// ESTRATEGIA DE SECCIÓN:
/// Los PDFs bancarios contienen texto legal, cabeceras, totales, publicidad.
/// No podemos parsear todas las líneas indiscriminadamente.
///
/// Usamos una máquina de estados simple:
///   SCANNING  → buscamos el marcador de inicio de consumos
///   IN_SECTION → parseamos líneas de transacción
///   DONE      → llegamos al marcador de fin (totales, próximo vencimiento, etc.)
///
/// Marcadores observados en BBVA:
///   Inicio: "DETALLE DE CONSUMOS" | "CONSUMOS DEL PERÍODO" | "DETALLE DE OPERACIONES"
///   Fin:    "TOTAL DE CONSUMOS"   | "SALDO ANTERIOR"       | "PRÓXIMO VENCIMIENTO"
/// </summary>
public sealed class BbvaVisaStatementParser : IStatementParser, IFileParser
{
    private readonly BbvaTransactionLineParser _lineParser;
    private readonly ILogger<BbvaVisaStatementParser> _logger;
    private readonly IPdfTextExtractor _textExtractor;

    public string ParserId => "BBVA_VISA_AR";

    // Section start markers: anclados al inicio de línea; "CONSUMOS" puede llevar nombre
    private static readonly Regex[] SectionStartMarkers =
    [
        new(@"^\s*DETALLE\s+DE\s+CONSUMOS\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*CONSUMOS\s+DEL\s+PER[IÍ]ODO\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*DETALLE\s+DE\s+OPERACIONES\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*SUS\s+CONSUMOS\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Permite "CONSUMOS" o "CONSUMOS <nombre>"
        new(@"^\s*CONSUMOS(\s+.*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    private static readonly Regex[] SectionEndMarkers =
    [
        // Detecta líneas que empiezan por "TOTAL CONSUMOS" o "TOTAL DE CONSUMOS"
        new(@"^\s*TOTAL\s+(DE\s+)?CONSUMOS\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*PR[OÓ]XIMO\s+VENCIMIENTO\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*SALDO\s+ANTERIOR\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*RESUMEN\s+DE\s+CUENTA\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*IMPORTANTE\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    // ──────────────────────────────────────────────────────────────
    // FINGERPRINT: identifica que el documento es BBVA
    // ──────────────────────────────────────────────────────────────

    private static readonly Regex[] BbvaFingerprints =
    [
        new(@"BBVA", RegexOptions.Compiled),
        new(@"Banco\s+BBVA", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"VISA\s+BBVA", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    public BbvaVisaStatementParser(
        BbvaTransactionLineParser lineParser,
        IPdfTextExtractor textExtractor,
        ILogger<BbvaVisaStatementParser> logger)
    {
        _lineParser = lineParser;
        _logger = logger;
        _textExtractor = textExtractor;
    }

    public bool CanHandle(IReadOnlyList<string> documentLines)
    {
        // Buscamos fingerprint en las primeras 30 líneas (cabecera del documento)
        var header = documentLines.Take(30);
        return header.Any(line => BbvaFingerprints.Any(fp => fp.IsMatch(line)));
    }

    public Task<IReadOnlyList<Transaction>> ParseAsync(
        IReadOnlyList<string> documentLines,
        string sourceFile,
        CancellationToken ct = default)
    {
        var transactions = new List<Transaction>();
        var parsingState = ParsingState.Scanning;
        var lineNumber = 0;
        var skippedLines = 0;
        var failedLines = new List<(int Line, string Text, string Reason)>();

        foreach (var line in documentLines)
        {
            ct.ThrowIfCancellationRequested();
            lineNumber++;

            switch (parsingState)
            {
                case ParsingState.Scanning:
                    if (IsSectionStart(line))
                    {
                        _logger.LogDebug("Sección de consumos detectada en línea {LineNum}: '{Line}'", lineNumber, line);
                        parsingState = ParsingState.InSection;
                    }
                    break;

                case ParsingState.InSection:
                    if (IsSectionEnd(line))
                    {
                        _logger.LogDebug("Fin de sección detectado en línea {LineNum}: '{Line}'", lineNumber, line);
                        parsingState = ParsingState.Done;
                        break;
                    }

                    if (!_lineParser.IsTransactionLine(line))
                    {
                        skippedLines++;
                        _logger.LogTrace("Línea ignorada [{LineNum}]: '{Line}'", lineNumber, line);
                        break;
                    }

                    var result = _lineParser.ParseTransaction(line);

                    if (result.Success && result.Value is not null)
                    {
                        result.Value.SourceFile = sourceFile;
                        transactions.Add(result.Value);
                        _logger.LogDebug(
                            "Transacción parseada [{LineNum}]: {Date} | {Desc} | {Currency} {Amount}",
                            lineNumber,
                            result.Value.Date.ToString("dd-MMM-yy"),
                            result.Value.Description,
                            result.Value.Currency,
                            result.Value.Amount);
                    }
                    else
                    {
                        failedLines.Add((lineNumber, line, result.Error ?? "Unknown"));
                        _logger.LogWarning(
                            "Error parseando línea [{LineNum}]: '{Line}' → {Error}",
                            lineNumber, line, result.Error);
                    }
                    break;

                case ParsingState.Done:
                    // Una vez llegamos al fin, paramos.
                    // Si el PDF tuviera múltiples secciones (ej: titular + adicional),
                    // aquí habría que volver a Scanning.
                    break;
            }
        }

        // Resumen de parsing para observabilidad
        _logger.LogInformation(
            "[{ParserId}] {Source}: {TxCount} transacciones | {Skipped} líneas ignoradas | {Failed} fallos",
            ParserId, Path.GetFileName(sourceFile),
            transactions.Count, skippedLines, failedLines.Count);

        if (failedLines.Count > 0)
        {
            _logger.LogWarning(
                "[{ParserId}] Líneas que fallaron el parseo:\n{Lines}",
                ParserId,
                string.Join("\n", failedLines.Select(f => $"  L{f.Line}: {f.Text} → {f.Reason}")));
        }

        if (parsingState == ParsingState.Scanning)
        {
            _logger.LogError(
                "[{ParserId}] NO se encontró sección de consumos en {Source}. " +
                "Verificar marcadores de sección o estructura del PDF.",
                ParserId, sourceFile);
        }

        return Task.FromResult<IReadOnlyList<Transaction>>(transactions.AsReadOnly());
    }

    // ──────────────────────────────────────────────────────────────
    // HELPERS
    // ──────────────────────────────────────────────────────────────

    private static bool IsSectionStart(string line) =>
        SectionStartMarkers.Any(m => m.IsMatch(line));

    private static bool IsSectionEnd(string line) =>
        SectionEndMarkers.Any(m => m.IsMatch(line));

    private enum ParsingState { Scanning, InSection, Done }

    // Supported extensions: sintaxis válida de C#
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".pdf" };

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
            _logger.LogError(ex, "Error extrayendo texto de {FilePath}", filePath);
            return new FileParseResult([], 0, [$"Extracción fallida: {ex.Message}"], sw.Elapsed);
        }

        if (!CanHandle(lines))
        {
            _logger.LogWarning(
                "El PDF {FilePath} no fue reconocido como extracto BBVA",
                filePath);
            return new FileParseResult([], 0,
                ["PDF no reconocido como extracto BBVA. Verificar fingerprints."],
                sw.Elapsed);
        }

        var transactions = await ParseAsync(lines, filePath, ct);

        var extracted = transactions.Select(t => new ExtractedTransaction(
            t.Date,
            t.Description,
            t.Amount,
            t.Currency,
            t.CouponNumber,
            t.RawLine,
            t.SourceFile
            )).ToList();

        sw.Stop();
        return new FileParseResult(extracted, 0, [], sw.Elapsed);
    }
}
