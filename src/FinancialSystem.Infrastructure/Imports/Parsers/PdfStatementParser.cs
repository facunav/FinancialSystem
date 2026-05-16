using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using FinancialSystem.Application.Imports;
using FinancialSystem.Application.Imports.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;

namespace FinancialSystem.Infrastructure.Imports.Parsers;

internal sealed class PdfStatementParser(
    IOptions<PdfStatementParseOptions> options,
    ILogger<PdfStatementParser> logger) : IFileParser
{
    private readonly Lazy<Regex[]> _linePatterns = new(() => CompilePatterns(options.Value));

    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".pdf"];

    public Task<FileParseResult> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var transactions = new List<ExtractedTransaction>();
        var diagnostics = new List<string>();
        var skipped = 0;
        var opts = options.Value;
        var patterns = _linePatterns.Value;

        var fullText = ExtractFullText(filePath);
        logger.LogDebug("PDF {FilePath} extracted {CharCount} characters", filePath, fullText.Length);

        var lineNumber = 0;
        foreach (var rawLine in fullText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;

            var line = CollapseSpaces(rawLine.Trim());
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (ShouldIgnoreLine(line, opts))
            {
                logger.LogTrace("PDF {FilePath} line {Line} ignored (heuristic): {Preview}", filePath, lineNumber, Truncate(line, 80));
                continue;
            }

            if (!TryParseTransactionLine(line, patterns, opts, out var extracted))
            {
                if (LooksLikePartialTransaction(line))
                {
                    skipped++;
                    diagnostics.Add($"Line {lineNumber}: no pattern match — {Truncate(line, 120)}");
                    logger.LogDebug("PDF {FilePath} line {Line} not matched: {Preview}", filePath, lineNumber, Truncate(line, 80));
                }

                continue;
            }

            transactions.Add(extracted with { SourceLocation = $"line:{lineNumber}" });
        }

        sw.Stop();
        logger.LogInformation(
            "PDF parser extracted {Count} transactions from {FilePath} in {ElapsedMs}ms ({Skipped} unmatched candidate lines)",
            transactions.Count,
            filePath,
            sw.ElapsedMilliseconds,
            skipped);

        return Task.FromResult(new FileParseResult(transactions, skipped, diagnostics, sw.Elapsed));
    }

    private static string ExtractFullText(string filePath)
    {
        var builder = new StringBuilder();
        using var document = PdfDocument.Open(filePath);
        foreach (var page in document.GetPages())
        {
            builder.AppendLine(page.Text);
        }

        return builder.ToString();
    }

    private static bool ShouldIgnoreLine(string line, PdfStatementParseOptions opts)
    {
        if (line.Length < opts.MinDescriptionLength)
        {
            return true;
        }

        foreach (var token in opts.IgnoreLineContains)
        {
            if (line.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (Regex.IsMatch(line, @"^\s*(TOTAL|SALDO|SUBTOTAL|RESUMEN)\b", RegexOptions.IgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikePartialTransaction(string line) =>
        Regex.IsMatch(line, @"\d{2}[/\-\.]\d{2}") && Regex.IsMatch(line, @"[\d.,]+");

    private static bool TryParseTransactionLine(
        string line,
        Regex[] patterns,
        PdfStatementParseOptions opts,
        out ExtractedTransaction extracted)
    {
        extracted = default!;

        foreach (var pattern in patterns)
        {
            var match = pattern.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var dateRaw = match.Groups["date"].Value;
            var desc = match.Groups["desc"].Value.Trim();
            var amountRaw = match.Groups["amount"].Value;

            if (desc.Length < opts.MinDescriptionLength || desc.Length > opts.MaxDescriptionLength)
            {
                continue;
            }

            if (!ImportValueParser.TryParseDate(dateRaw, out var date))
            {
                continue;
            }

            if (!ImportValueParser.TryParseAmount(amountRaw, out var amount))
            {
                continue;
            }

            var currency = ImportValueParser.DetectCurrencyFromText(desc);
            extracted = new ExtractedTransaction(date, desc, amount, currency);
            return true;
        }

        return false;
    }

    private static Regex[] CompilePatterns(PdfStatementParseOptions opts)
    {
        var list = new List<Regex>();
        foreach (var pattern in opts.LinePatterns)
        {
            try
            {
                list.Add(new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant));
            }
            catch
            {
                // skip invalid patterns from config
            }
        }

        if (list.Count == 0)
        {
            list.Add(new Regex(
                @"^(?<date>\d{2}/\d{2}/\d{2,4})\s+(?<desc>.+?)\s+(?<amount>-?[\d.,]+)\s*$",
                RegexOptions.Compiled | RegexOptions.CultureInvariant));
        }

        return list.ToArray();
    }

    private static string CollapseSpaces(string line) =>
        Regex.Replace(line, @"\s{2,}", " ");

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
