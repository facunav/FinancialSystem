using FinancialSystem.Application.Suggestions;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Infrastructure.Suggestions;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Suggestions;

/// <summary>
/// Cubre la resolución de historial conflictivo agregada en PR-S10 a
/// <see cref="ClassificationSuggestionService.BuildSuggestions"/>: qué valor se propone
/// (la moda, no el más reciente) y qué <see cref="SuggestionConfidence"/> le corresponde
/// (High solo unánime; Medium con mayoría calificada de 2/3; Low por debajo de eso,
/// incluyendo empates). BuildSuggestions es internal exclusivamente para que este
/// proyecto de tests pueda invocarlo vía InternalsVisibleTo — no es parte del contrato
/// público del motor de sugerencias.
/// </summary>
public class ClassificationSuggestionServiceBuildSuggestionsTests
{
    private static readonly Guid CategoryA = Guid.Parse("00000000-0000-0000-0000-0000000000A1");
    private static readonly Guid CategoryB = Guid.Parse("00000000-0000-0000-0000-0000000000B2");
    private static readonly Guid CounterpartyA = Guid.Parse("00000000-0000-0000-0000-0000000000C3");
    private static readonly DateTime BaseDate = new(2026, 1, 1);

    private static ClassificationSuggestionService.ClassifiedHistoryRow Row(
        Guid categoryId, DateTime processedAt,
        MovementType movementType = MovementType.Purchase,
        FinancialImpact financialImpact = FinancialImpact.Expense,
        Guid? counterpartyId = null) =>
        new("DESCRIPCION", categoryId, movementType, financialImpact, counterpartyId, processedAt);

    [Fact]
    public void BuildSuggestions_FullyConsistentHistory_YieldsHighConfidenceForEveryDimension()
    {
        var matches = Enumerable.Range(0, 5)
            .Select(i => Row(CategoryA, BaseDate.AddDays(i), counterpartyId: CounterpartyA))
            .ToList();

        var suggestions = ClassificationSuggestionService.BuildSuggestions(matches);

        Assert.Equal(4, suggestions.Count);
        Assert.All(suggestions, s => Assert.Equal(SuggestionConfidence.High, s.Confidence));
        Assert.Equal(CategoryA, Assert.Single(suggestions, s => s.Dimension == SuggestionDimension.Category).Value);
    }

    [Fact]
    public void BuildSuggestions_OverwhelmingMajority_ProposesMajorityValueWithMediumConfidence_EvenWhenMinorityIsMostRecent()
    {
        // 99 clasificaciones historicas de CategoryA (mas antiguas) y 1 sola de
        // CategoryB, que ademas es la mas reciente de todas - a proposito, para probar
        // que PR-S10 propone la mayoria (CategoryA) y no simplemente "la ultima vez"
        // (CategoryB), que era el comportamiento hasta PR-S9.
        var matches = Enumerable.Range(0, 99)
            .Select(i => Row(CategoryA, BaseDate.AddDays(i), counterpartyId: CounterpartyA))
            .Append(Row(CategoryB, BaseDate.AddDays(200), counterpartyId: CounterpartyA))
            .ToList();

        var suggestions = ClassificationSuggestionService.BuildSuggestions(matches);

        var category = Assert.Single(suggestions, s => s.Dimension == SuggestionDimension.Category);
        Assert.Equal(CategoryA, category.Value);
        Assert.Equal(SuggestionConfidence.Medium, category.Confidence);
        Assert.Contains("mayoría amplia", category.Reason);
        Assert.Contains("99 de 100", category.Reason);

        // MovementType/FinancialImpact/Counterparty son unánimes en las 100 filas.
        Assert.Equal(SuggestionConfidence.High, Assert.Single(suggestions, s => s.Dimension == SuggestionDimension.MovementType).Confidence);
        Assert.Equal(SuggestionConfidence.High, Assert.Single(suggestions, s => s.Dimension == SuggestionDimension.FinancialImpact).Confidence);
        Assert.Equal(SuggestionConfidence.High, Assert.Single(suggestions, s => s.Dimension == SuggestionDimension.Counterparty).Confidence);
    }

    [Fact]
    public void BuildSuggestions_ExactlyTwoThirds_IsStillMediumAtTheBoundary()
    {
        var matches = new List<ClassificationSuggestionService.ClassifiedHistoryRow>
        {
            Row(CategoryA, BaseDate),
            Row(CategoryA, BaseDate.AddDays(1)),
            Row(CategoryB, BaseDate.AddDays(2)),
        };

        var suggestions = ClassificationSuggestionService.BuildSuggestions(matches);

        var category = Assert.Single(suggestions, s => s.Dimension == SuggestionDimension.Category);
        Assert.Equal(CategoryA, category.Value);
        Assert.Equal(SuggestionConfidence.Medium, category.Confidence);
    }

    [Fact]
    public void BuildSuggestions_DividedHistoryWithoutSupermajority_YieldsLowConfidence()
    {
        // 60 CategoryA / 40 CategoryB: CategoryA gana claramente (no es empate), pero
        // 60% queda por debajo del umbral de mayoria calificada (2/3 ~= 66.7%).
        var matches = Enumerable.Range(0, 60)
            .Select(i => Row(CategoryA, BaseDate.AddDays(i)))
            .Concat(Enumerable.Range(0, 40).Select(i => Row(CategoryB, BaseDate.AddDays(100 + i))))
            .ToList();

        var suggestions = ClassificationSuggestionService.BuildSuggestions(matches);

        var category = Assert.Single(suggestions, s => s.Dimension == SuggestionDimension.Category);
        Assert.Equal(CategoryA, category.Value);
        Assert.Equal(SuggestionConfidence.Low, category.Confidence);
        Assert.Contains("sin mayoría clara", category.Reason);
        Assert.Contains("60 de 100", category.Reason);
    }

    [Fact]
    public void BuildSuggestions_TiedHistory_YieldsLowConfidenceAndBreaksTieByRecency()
    {
        // 50 CategoryA / 50 CategoryB, empate total en cantidad - CategoryB tiene la
        // clasificacion mas reciente de las dos, asi que gana el desempate.
        var matches = Enumerable.Range(0, 50)
            .Select(i => Row(CategoryA, BaseDate.AddDays(i)))
            .Concat(Enumerable.Range(0, 50).Select(i => Row(CategoryB, BaseDate.AddDays(100 + i))))
            .ToList();

        var suggestions = ClassificationSuggestionService.BuildSuggestions(matches);

        var category = Assert.Single(suggestions, s => s.Dimension == SuggestionDimension.Category);
        Assert.Equal(CategoryB, category.Value);
        Assert.Equal(SuggestionConfidence.Low, category.Confidence);
    }

    [Fact]
    public void BuildSuggestions_NoHistory_ReturnsNoSuggestions()
    {
        var suggestions = ClassificationSuggestionService.BuildSuggestions([]);

        Assert.Empty(suggestions);
    }

    [Fact]
    public void BuildSuggestions_NoCounterpartyInHistory_OmitsCounterpartyDimensionOnly()
    {
        var matches = Enumerable.Range(0, 3)
            .Select(i => Row(CategoryA, BaseDate.AddDays(i), counterpartyId: null))
            .ToList();

        var suggestions = ClassificationSuggestionService.BuildSuggestions(matches);

        Assert.Equal(3, suggestions.Count);
        Assert.DoesNotContain(suggestions, s => s.Dimension == SuggestionDimension.Counterparty);
    }
}
