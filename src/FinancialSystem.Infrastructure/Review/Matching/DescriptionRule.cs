using FinancialSystem.Application.Review;
using FinancialSystem.Domain.Review;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Infrastructure.Review.Matching;

/// <summary>
/// Compara las descripciones de dos movimientos por similitud de texto (distancia
/// de Levenshtein normalizada). Por debajo de <see cref="ReviewEngineOptions.DescriptionMinimumSimilarity"/>
/// se considera que las descripciones no guardan relación y el score es 0.
/// </summary>
internal sealed class DescriptionRule : IMatchingRule
{
    private readonly ReviewEngineOptions _options;

    public DescriptionRule(IOptions<ReviewEngineOptions> options) => _options = options.Value;

    public MatchRuleKind Kind => MatchRuleKind.Description;

    public double Evaluate(FinancialMovement reference, FinancialMovement candidate)
    {
        var similarity = Similarity(Normalize(reference.Description), Normalize(candidate.Description));
        return similarity < _options.DescriptionMinimumSimilarity ? 0.0 : similarity;
    }

    private static string Normalize(string description) =>
        string.Join(' ', description.ToLowerInvariant().Split(
            ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>1.0 = idénticas, 0.0 = completamente distintas. Basado en distancia de Levenshtein.</summary>
    private static double Similarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0) return 1.0;
        if (a.Length == 0 || b.Length == 0) return 0.0;

        var maxLength = Math.Max(a.Length, b.Length);
        var distance = LevenshteinDistance(a, b);
        return 1.0 - (double)distance / maxLength;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var previousRow = new int[b.Length + 1];
        var currentRow = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++) previousRow[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            currentRow[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                currentRow[j] = Math.Min(
                    Math.Min(currentRow[j - 1] + 1, previousRow[j] + 1),
                    previousRow[j - 1] + cost);
            }

            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[b.Length];
    }
}
