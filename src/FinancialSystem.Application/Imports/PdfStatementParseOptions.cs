namespace FinancialSystem.Application.Imports;

/// <summary>Configurable heuristics for PDF bank/card statement parsing.</summary>
public sealed class PdfStatementParseOptions
{
    public const string SectionName = "FileIngestion:Pdf";

    /// <summary>Substrings that cause a line to be skipped (case-insensitive).</summary>
    public List<string> IgnoreLineContains { get; set; } =
    [
        "RESUMEN DE CUENTA",
        "TOTAL A PAGAR",
        "SALDO ANTERIOR",
        "SALDO ACTUAL",
        "PAGINA",
        "PÁGINA",
        "LEGALES",
        "CONSUMOS DEL PERIODO",
        "TNA ",
        "TEA ",
        "CFT ",
        "CUIT ",
        "IVA ",
        "PERCEPCION",
        "PERCEPCIÓN"
    ];

    /// <summary>Regex patterns (named groups: date, desc, amount). Applied in order.</summary>
    public List<string> LinePatterns { get; set; } =
    [
        @"^(?<date>\d{2}/\d{2}/\d{2,4})\s+(?<desc>.+?)\s+(?<amount>-?[\d.,]+)\s*$",
        @"^(?<date>\d{2}-\d{2}-\d{2,4})\s+(?<desc>.+?)\s+(?<amount>-?[\s\d.,]+)\s*$",
        @"^(?<date>\d{2}\.\d{2}\.\d{2,4})\s+(?<desc>.+?)\s+(?<amount>-?[\d.,]+)\s*$"
    ];

    public int MinDescriptionLength { get; set; } = 2;

    public int MaxDescriptionLength { get; set; } = 512;
}
