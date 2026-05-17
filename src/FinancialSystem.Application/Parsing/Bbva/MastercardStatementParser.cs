using System.Text.RegularExpressions;
using FinancialSystem.Application.Imports;
using FinancialSystem.Application.Imports.Parsing;
using FinancialSystem.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Application.Parsing.Mastercard;

/// <summary>
/// Parser de extractos Mastercard Argentina.
/// Hereda toda la infraestructura de máquina de estados de PdfStatementParserBase.
///
/// FINGERPRINTS observados en extractos Mastercard AR:
///   - "MASTERCARD" o "Master Card" en cabecera
///   - Emisores comunes: "Galicia Mastercard", "ICBC Mastercard", "Naranja X Mastercard"
///   - Número de tarjeta enmascarado: **** **** **** XXXX
///
/// SECCIONES observadas:
///   Inicio: "DETALLE DE CONSUMOS" | "CONSUMOS" | "OPERACIONES DEL PERÍODO"
///   Fin:    "TOTAL" | "PRÓXIMO VENCIMIENTO" | "SALDO ANTERIOR" | "CUOTAS PENDIENTES"
///
/// NOTA IMPORTANTE sobre fingerprints:
///   Si el extracto tiene "BBVA" Y "MASTERCARD" (ej: BBVA Mastercard),
///   el orden de registro en DI determina qué parser gana.
///   Por diseño, BBVA Visa se registra primero porque es más específico.
///   Si necesitás parsear BBVA Mastercard, crear un parser dedicado
///   BbvaMastercardStatementParser con fingerprints más específicos.
/// </summary>
public sealed class MastercardStatementParser : PdfStatementParserBase
{
    private readonly MastercardTransactionLineParser _lineParser;

    public MastercardStatementParser(
        MastercardTransactionLineParser lineParser,
        IPdfTextExtractor textExtractor,
        ILogger<MastercardStatementParser> logger)
        : base(textExtractor, logger)
    {
        _lineParser = lineParser;
    }

    public override string ParserId => "MASTERCARD_AR";

    // ── Fingerprints ──────────────────────────────────────────────
    // Buscamos combinaciones que confirmen que es un extracto Mastercard,
    // no solo cualquier documento que mencione la marca.

    private static readonly Regex[] _fingerprints =
    [
        // "MASTERCARD" como palabra standalone (extractos genéricos)
        new(@"\bMASTERCARD\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // "Master Card" en dos palabras (variante tipográfica)
        new(@"\bMASTER\s+CARD\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Emisores conocidos con Mastercard
        new(@"GALICIA\s+MASTERCARD", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"ICBC\s+MASTERCARD", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"NARANJA\s+(?:X\s+)?MASTERCARD", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"SANTANDER\s+MASTERCARD", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"MACRO\s+MASTERCARD", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    protected override IReadOnlyList<Regex> Fingerprints => _fingerprints;

    // ── Marcadores de sección ────────────────────────────────────

    private static readonly Regex[] _sectionStart =
    [
        new(@"^\s*DETALLE\s+DE\s+CONSUMOS\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*CONSUMOS\s+DEL\s+PER[IÍ]ODO\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*OPERACIONES\s+DEL\s+PER[IÍ]ODO\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*CONSUMOS\s+REALIZADOS\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // "CONSUMOS" solo (como en algunos emisores)
        new(@"^\s*CONSUMOS\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Tabla con encabezado de columnas (señal de que vienen transacciones)
        new(@"^\s*FECHA\s+DESCRIPCI[OÓ]N\s+.*IMPORTE", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    private static readonly Regex[] _sectionEnd =
    [
        new(@"^\s*TOTAL\s+(DE\s+)?CONSUMOS\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*TOTAL\s+A\s+PAGAR\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*PR[OÓ]XIMO\s+VENCIMIENTO\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*SALDO\s+ANTERIOR\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*CUOTAS\s+PENDIENTES\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*RESUMEN\s+DE\s+CUENTA\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*IMPORTANTE\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*INFORMACI[OÓ]N\s+IMPORTANTE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    protected override IReadOnlyList<Regex> SectionStartPatterns => _sectionStart;
    protected override IReadOnlyList<Regex> SectionEndPatterns => _sectionEnd;

    // ── Delegación al line parser ────────────────────────────────

    protected override bool IsTransactionLine(string line) =>
        _lineParser.IsTransactionLine(line);

    protected override ParseResult<Transaction> ParseLine(string line) =>
        _lineParser.ParseLine(line);
}
