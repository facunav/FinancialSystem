using System.Globalization;
using System.Text.RegularExpressions;
using FinancialSystem.Application.Imports;
using FinancialSystem.Application.Imports.Parsing;

namespace FinancialSystem.Infrastructure.Imports.Normalization;

internal sealed partial class TransactionNormalizer : ITransactionNormalizer
{
    private static readonly Regex WhitespaceCollapse = WhitespaceCollapseRegex();

    public ParsedTransaction Normalize(ExtractedTransaction extracted)
    {
        var description = CleanDescription(extracted.RawDescription);
        var amount = extracted.Amount;
        var currency = ResolveCurrency(extracted.CurrencyHint, description);
        var date = NormalizeDate(extracted.Date);
        var couponNumber = extracted.CouponNumber;
        var rawLine = extracted.RawLine;
        var sourceFile = extracted.SourceLocation;
        var accountNumber = extracted.AccountNumber;

        return new ParsedTransaction(date, description, amount, currency, couponNumber, rawLine, sourceFile, accountNumber);
    }

    public IReadOnlyList<ParsedTransaction> NormalizeAll(IEnumerable<ExtractedTransaction> extracted) =>
        extracted.Select(Normalize).ToList();

    private static string CleanDescription(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        trimmed = WhitespaceCollapse.Replace(trimmed, " ");
        return trimmed.Length > 512 ? trimmed[..512] : trimmed;
    }

    private static string ResolveCurrency(string? hint, string description)
    {
        if (!string.IsNullOrWhiteSpace(hint))
        {
            var code = hint.Trim().ToUpperInvariant();
            return code.Length <= 3 ? code : code[..3];
        }

        return ImportValueParser.DetectCurrencyFromText(description) ?? "ARS";
    }

    private static DateTime NormalizeDate(DateTime date) =>
        DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceCollapseRegex();
}
