using System.Text.RegularExpressions;
using FinancialSystem.Application.Imports;
using FinancialSystem.Application.Imports.Parsing;
using FinancialSystem.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Application.Parsing.Bbva;

/// <summary>
/// Parser para extractos BBVA Visa Argentina.
/// Refactorizado para heredar de PdfStatementParserBase:
///   - Elimina duplicación de máquina de estados
///   - Mantiene exactamente el mismo comportamiento observable
///   - Fingerprints y marcadores sin cambios
/// </summary>
public sealed class BbvaVisaStatementParser : PdfStatementParserBase
{
    private readonly BbvaTransactionLineParser _lineParser;

    public BbvaVisaStatementParser(
        BbvaTransactionLineParser lineParser,
        IPdfTextExtractor textExtractor,
        ILogger<BbvaVisaStatementParser> logger)
        : base(textExtractor, logger)
    {
        _lineParser = lineParser;
    }

    public override string ParserId => "BBVA_VISA_AR";

    // ── Fingerprints ──────────────────────────────────────────────
    // Combinaciones específicas de BBVA + Visa para no colisionar con
    // futuros parsers de BBVA Mastercard.

    private static readonly Regex[] _fingerprints =
    [
        new(@"\bBBVA\b", RegexOptions.Compiled),
        new(@"Banco\s+BBVA", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"VISA\s+BBVA", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"BBVA\s+VISA", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    protected override IReadOnlyList<Regex> Fingerprints => _fingerprints;

    // ── Marcadores de sección ─────────────────────────────────────

    private static readonly Regex[] _sectionStart =
    [
        new(@"^\s*DETALLE\s+DE\s+CONSUMOS\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*CONSUMOS\s+DEL\s+PER[IÍ]ODO\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*DETALLE\s+DE\s+OPERACIONES\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*SUS\s+CONSUMOS\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*CONSUMOS(\s+.*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    private static readonly Regex[] _sectionEnd =
    [
        new(@"^\s*TOTAL\s+(DE\s+)?CONSUMOS\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*PR[OÓ]XIMO\s+VENCIMIENTO\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*SALDO\s+ANTERIOR\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*RESUMEN\s+DE\s+CUENTA\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*IMPORTANTE\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    protected override IReadOnlyList<Regex> SectionStartPatterns => _sectionStart;
    protected override IReadOnlyList<Regex> SectionEndPatterns => _sectionEnd;

    // ── Delegación al line parser existente ──────────────────────
    // BbvaTransactionLineParser no cambia en absoluto.

    protected override bool IsTransactionLine(string line) =>
        _lineParser.IsTransactionLine(line);

    protected override ParseResult<Transaction> ParseLine(string line) =>
        _lineParser.ParseTransaction(line);
}
