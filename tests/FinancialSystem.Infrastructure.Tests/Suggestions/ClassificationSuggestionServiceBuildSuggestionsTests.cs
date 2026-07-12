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
/// incluyendo empates). También cubre la exclusión de categorías/contrapartes
/// desactivadas agregada en PR-S11: esos valores no deben participar de la frecuencia
/// ni de la confianza de su propia dimensión, sin afectar a las demás. BuildSuggestions
/// es internal exclusivamente para que este proyecto de tests pueda invocarlo vía
/// InternalsVisibleTo — no es parte del contrato público del motor de sugerencias.
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
        Guid? counterpartyId = null,
        bool categoryIsDeactivated = false,
        bool counterpartyIsDeactivated = false) =>
        new("DESCRIPCION", categoryId, movementType, financialImpact, counterpartyId, processedAt,
            categoryIsDeactivated, counterpartyIsDeactivated);

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

    [Fact]
    public void BuildSuggestions_OnlyDeactivatedCategoryInHistory_NeverSuggestsCategory_OtherDimensionsUnaffected()
    {
        var matches = Enumerable.Range(0, 5)
            .Select(i => Row(CategoryA, BaseDate.AddDays(i), counterpartyId: CounterpartyA, categoryIsDeactivated: true))
            .ToList();

        var suggestions = ClassificationSuggestionService.BuildSuggestions(matches);

        Assert.DoesNotContain(suggestions, s => s.Dimension == SuggestionDimension.Category);

        // MovementType/FinancialImpact/Counterparty son independientes de Category y
        // siguen siendo evidencia valida (misma fila, mismo IsDeactivated de Category
        // no las afecta) - ver doc-comment de BuildSuggestions.
        Assert.Equal(SuggestionConfidence.High, Assert.Single(suggestions, s => s.Dimension == SuggestionDimension.MovementType).Confidence);
        Assert.Equal(SuggestionConfidence.High, Assert.Single(suggestions, s => s.Dimension == SuggestionDimension.FinancialImpact).Confidence);
        Assert.Equal(SuggestionConfidence.High, Assert.Single(suggestions, s => s.Dimension == SuggestionDimension.Counterparty).Confidence);
    }

    [Fact]
    public void BuildSuggestions_OnlyDeactivatedCounterpartyInHistory_NeverSuggestsCounterparty_OtherDimensionsUnaffected()
    {
        var matches = Enumerable.Range(0, 5)
            .Select(i => Row(CategoryA, BaseDate.AddDays(i), counterpartyId: CounterpartyA, counterpartyIsDeactivated: true))
            .ToList();

        var suggestions = ClassificationSuggestionService.BuildSuggestions(matches);

        Assert.DoesNotContain(suggestions, s => s.Dimension == SuggestionDimension.Counterparty);

        Assert.Equal(SuggestionConfidence.High, Assert.Single(suggestions, s => s.Dimension == SuggestionDimension.Category).Confidence);
        Assert.Equal(SuggestionConfidence.High, Assert.Single(suggestions, s => s.Dimension == SuggestionDimension.MovementType).Confidence);
        Assert.Equal(SuggestionConfidence.High, Assert.Single(suggestions, s => s.Dimension == SuggestionDimension.FinancialImpact).Confidence);
    }

    [Fact]
    public void BuildSuggestions_ActiveCategoryAndCounterparty_BehavesExactlyAsWithoutDeactivation()
    {
        var matches = Enumerable.Range(0, 5)
            .Select(i => Row(
                CategoryA, BaseDate.AddDays(i), counterpartyId: CounterpartyA,
                categoryIsDeactivated: false, counterpartyIsDeactivated: false))
            .ToList();

        var suggestions = ClassificationSuggestionService.BuildSuggestions(matches);

        Assert.Equal(4, suggestions.Count);
        Assert.All(suggestions, s => Assert.Equal(SuggestionConfidence.High, s.Confidence));
        Assert.Equal(CategoryA, Assert.Single(suggestions, s => s.Dimension == SuggestionDimension.Category).Value);
        Assert.Equal(CounterpartyA, Assert.Single(suggestions, s => s.Dimension == SuggestionDimension.Counterparty).Value);
    }

    [Fact]
    public void BuildSuggestions_DeactivatedMajority_ConfidenceComesOnlyFromActiveMinority()
    {
        // Sin el filtro de PR-S11: CategoryA (desactivada) seria mayoria calificada
        // (10 de 13 ~= 77% >= 2/3) y se propondria con Medium - exactamente el bug que
        // este PR corrige. Con el filtro: las 10 filas de CategoryA se excluyen antes
        // de contar, quedan solo las 3 de CategoryB (activa), que pasan a ser unanimes
        // dentro del subconjunto filtrado -> High, no Medium, y CategoryB, no CategoryA.
        var matches = Enumerable.Range(0, 10)
            .Select(i => Row(CategoryA, BaseDate.AddDays(i), categoryIsDeactivated: true))
            .Concat(Enumerable.Range(0, 3).Select(i => Row(CategoryB, BaseDate.AddDays(100 + i))))
            .ToList();

        var suggestions = ClassificationSuggestionService.BuildSuggestions(matches);

        var category = Assert.Single(suggestions, s => s.Dimension == SuggestionDimension.Category);
        Assert.Equal(CategoryB, category.Value);
        Assert.Equal(SuggestionConfidence.High, category.Confidence);
        Assert.Contains("3 clasificaciones", category.Reason);
    }
}
