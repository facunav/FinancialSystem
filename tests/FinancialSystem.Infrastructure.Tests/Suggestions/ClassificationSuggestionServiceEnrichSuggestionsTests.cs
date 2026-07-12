using FinancialSystem.Application.Suggestions;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Infrastructure.Suggestions;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Suggestions;

/// <summary>
/// Cubre la heurística 2 (enriquecimiento vía <c>Counterparty.Default*</c>, PR-S7) a
/// través de <see cref="ClassificationSuggestionService.EnrichSuggestions"/> — sin
/// ningún test hasta PR-S12. Incluye la validación agregada en PR-S12
/// (<c>DefaultCategoryId</c> solo se propone si <c>DefaultCategoryIsActive</c>) y
/// confirma que <c>DefaultMovementType</c>/<c>DefaultFinancialImpact</c> y el criterio
/// de fusión de <c>MergeDimension</c> (mayor confianza gana, empate mantiene la
/// primera) siguen exactamente igual — ejercitado indirectamente a través de
/// <c>EnrichSuggestions</c>, sin tocar la accesibilidad de <c>MergeDimension</c> en sí.
/// <c>EnrichSuggestions</c>/<c>CounterpartyDefaultsRow</c> son internal exclusivamente
/// para este proyecto de tests vía InternalsVisibleTo — no son parte del contrato
/// público del motor de sugerencias.
/// </summary>
public class ClassificationSuggestionServiceEnrichSuggestionsTests
{
    private static readonly Guid CounterpartyId = Guid.Parse("00000000-0000-0000-0000-0000000000D4");
    private static readonly Guid CategoryId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");
    private static readonly Guid OtherCategoryId = Guid.Parse("00000000-0000-0000-0000-0000000000B2");

    private static ClassificationSuggestion CounterpartySuggestion(SuggestionConfidence confidence = SuggestionConfidence.High) =>
        new(SuggestionDimension.Counterparty, CounterpartyId, confidence, "Sugerencia historica de contraparte.");

    private static Dictionary<Guid, ClassificationSuggestionService.CounterpartyDefaultsRow> Defaults(
        Guid? defaultCategoryId = null,
        bool defaultCategoryIsActive = true,
        MovementType? defaultMovementType = null,
        FinancialImpact? defaultFinancialImpact = null) => new()
    {
        [CounterpartyId] = new(
            CounterpartyId, "Contraparte de prueba", defaultCategoryId, defaultCategoryIsActive,
            defaultMovementType, defaultFinancialImpact),
    };

    [Fact]
    public void EnrichSuggestions_ActiveDefaultCategory_IsSuggested()
    {
        var suggestions = new List<ClassificationSuggestion> { CounterpartySuggestion() };
        var defaults = Defaults(defaultCategoryId: CategoryId, defaultCategoryIsActive: true);

        var result = ClassificationSuggestionService.EnrichSuggestions(suggestions, defaults);

        var category = Assert.Single(result, s => s.Dimension == SuggestionDimension.Category);
        Assert.Equal(CategoryId, category.Value);
        Assert.Equal(SuggestionConfidence.High, category.Confidence);
    }

    [Fact]
    public void EnrichSuggestions_DeactivatedOrNonexistentDefaultCategory_IsNeverSuggested()
    {
        var suggestions = new List<ClassificationSuggestion> { CounterpartySuggestion() };
        var defaults = Defaults(defaultCategoryId: CategoryId, defaultCategoryIsActive: false);

        var result = ClassificationSuggestionService.EnrichSuggestions(suggestions, defaults);

        Assert.DoesNotContain(result, s => s.Dimension == SuggestionDimension.Category);
    }

    [Fact]
    public void EnrichSuggestions_DeactivatedDefaultCategory_DoesNotDisturbExistingHistoricalCategorySuggestion()
    {
        var historicalCategory = new ClassificationSuggestion(
            SuggestionDimension.Category, OtherCategoryId, SuggestionConfidence.Medium, "Sugerencia historica.");
        var suggestions = new List<ClassificationSuggestion> { historicalCategory, CounterpartySuggestion() };
        var defaults = Defaults(defaultCategoryId: CategoryId, defaultCategoryIsActive: false);

        var result = ClassificationSuggestionService.EnrichSuggestions(suggestions, defaults);

        var category = Assert.Single(result, s => s.Dimension == SuggestionDimension.Category);
        Assert.Equal(OtherCategoryId, category.Value);
        Assert.Equal(SuggestionConfidence.Medium, category.Confidence);
    }

    [Fact]
    public void EnrichSuggestions_DefaultMovementType_StillWorksUnaffectedByCategoryValidation()
    {
        var suggestions = new List<ClassificationSuggestion> { CounterpartySuggestion() };
        var defaults = Defaults(defaultMovementType: MovementType.Transfer);

        var result = ClassificationSuggestionService.EnrichSuggestions(suggestions, defaults);

        var movementType = Assert.Single(result, s => s.Dimension == SuggestionDimension.MovementType);
        Assert.Equal(MovementType.Transfer, movementType.Value);
        Assert.Equal(SuggestionConfidence.High, movementType.Confidence);
    }

    [Fact]
    public void EnrichSuggestions_DefaultFinancialImpact_StillWorksUnaffectedByCategoryValidation()
    {
        var suggestions = new List<ClassificationSuggestion> { CounterpartySuggestion() };
        var defaults = Defaults(defaultFinancialImpact: FinancialImpact.DebtPayment);

        var result = ClassificationSuggestionService.EnrichSuggestions(suggestions, defaults);

        var financialImpact = Assert.Single(result, s => s.Dimension == SuggestionDimension.FinancialImpact);
        Assert.Equal(FinancialImpact.DebtPayment, financialImpact.Value);
        Assert.Equal(SuggestionConfidence.High, financialImpact.Confidence);
    }

    [Fact]
    public void EnrichSuggestions_MergeDimension_DefaultReplacesLowerConfidenceHistoricalSuggestion()
    {
        var historicalCategory = new ClassificationSuggestion(
            SuggestionDimension.Category, OtherCategoryId, SuggestionConfidence.Medium, "Sugerencia historica.");
        var suggestions = new List<ClassificationSuggestion> { historicalCategory, CounterpartySuggestion() };
        var defaults = Defaults(defaultCategoryId: CategoryId, defaultCategoryIsActive: true);

        var result = ClassificationSuggestionService.EnrichSuggestions(suggestions, defaults);

        var category = Assert.Single(result, s => s.Dimension == SuggestionDimension.Category);
        Assert.Equal(CategoryId, category.Value);
        Assert.Equal(SuggestionConfidence.High, category.Confidence);
    }

    [Fact]
    public void EnrichSuggestions_MergeDimension_KeepsExistingHighConfidenceHistoricalSuggestionOnTie()
    {
        var historicalCategory = new ClassificationSuggestion(
            SuggestionDimension.Category, OtherCategoryId, SuggestionConfidence.High, "Sugerencia historica.");
        var suggestions = new List<ClassificationSuggestion> { historicalCategory, CounterpartySuggestion() };
        var defaults = Defaults(defaultCategoryId: CategoryId, defaultCategoryIsActive: true);

        var result = ClassificationSuggestionService.EnrichSuggestions(suggestions, defaults);

        var category = Assert.Single(result, s => s.Dimension == SuggestionDimension.Category);
        Assert.Equal(OtherCategoryId, category.Value);
        Assert.Equal(SuggestionConfidence.High, category.Confidence);
    }

    [Fact]
    public void EnrichSuggestions_NoCounterpartySuggestion_ReturnsUnchanged()
    {
        var suggestions = new List<ClassificationSuggestion>
        {
            new(SuggestionDimension.Category, CategoryId, SuggestionConfidence.High, "Sugerencia historica."),
        };
        var defaults = Defaults(defaultCategoryId: CategoryId, defaultCategoryIsActive: true);

        var result = ClassificationSuggestionService.EnrichSuggestions(suggestions, defaults);

        Assert.Same(suggestions, result);
    }

    [Fact]
    public void EnrichSuggestions_NoDefaultsConfigured_ReturnsUnchanged()
    {
        var suggestions = new List<ClassificationSuggestion> { CounterpartySuggestion() };
        var defaults = Defaults();

        var result = ClassificationSuggestionService.EnrichSuggestions(suggestions, defaults);

        Assert.Same(suggestions, result);
    }
}
