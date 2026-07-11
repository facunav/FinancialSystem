using FinancialSystem.Infrastructure.Suggestions;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Suggestions;

/// <summary>
/// Cubre las dos reglas determinísticas agregadas en PR-S9 a
/// <see cref="ClassificationSuggestionService.Normalize"/> (monto USD embebido, contador
/// de cuotas embebido) y el comportamiento previo de PR-S3 que debe seguir intacto.
/// Normalize es internal (no public) exclusivamente para que este proyecto de tests
/// pueda llamarlo vía InternalsVisibleTo — no es parte del contrato público del motor
/// de sugerencias.
/// </summary>
public class ClassificationSuggestionServiceNormalizeTests
{
    [Fact]
    public void Normalize_CollapsesEmbeddedUsdAmount_SoTheSameMerchantMatchesAcrossDifferentBillingAmounts()
    {
        var first = ClassificationSuggestionService.Normalize("PLAYSTATION USD 4,99");
        var second = ClassificationSuggestionService.Normalize("PLAYSTATION USD 9,99");

        Assert.Equal("PLAYSTATION", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Normalize_CollapsesInstallmentCounter_SoTheSameMerchantMatchesAcrossDifferentInstallments()
    {
        var first = ClassificationSuggestionService.Normalize("GARBARINO C1/3");
        var second = ClassificationSuggestionService.Normalize("GARBARINO C2/3");
        var third = ClassificationSuggestionService.Normalize("GARBARINO C3/3");

        Assert.Equal("GARBARINO", first);
        Assert.Equal(first, second);
        Assert.Equal(first, third);
    }

    [Fact]
    public void Normalize_StripsUsdAmountRegardlessOfSurroundingText()
    {
        var normalized = ClassificationSuggestionService.Normalize("NETFLIX.COM USD 11,14 SUSCRIPCION");

        Assert.Equal("NETFLIX.COM SUSCRIPCION", normalized);
    }

    [Theory]
    [InlineData("FARMACIA 24", "FARMACIA 24")]
    [InlineData("CANAL 13", "CANAL 13")]
    [InlineData("RUTA 8 SA", "RUTA 8 SA")]
    public void Normalize_DoesNotStripGenericDigitsThatAreNotUsdAmountsOrInstallmentCounters(
        string description, string expected)
    {
        Assert.Equal(expected, ClassificationSuggestionService.Normalize(description));
    }

    [Fact]
    public void Normalize_LeavesDescriptionsWithoutUsdOrInstallmentMarkersUnchanged()
    {
        var normalized = ClassificationSuggestionService.Normalize("DLO*PEDIDOSYA MCDONALD");

        Assert.Equal("DLO*PEDIDOSYA MCDONALD", normalized);
    }

    [Theory]
    [InlineData("mcdonald", "MCDONALD")]
    [InlineData("  MCDONALD  ", "MCDONALD")]
    [InlineData("MC   DONALD", "MC DONALD")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Normalize_PreservesExistingTrimUppercaseAndWhitespaceCollapseBehavior(string input, string expected)
    {
        Assert.Equal(expected, ClassificationSuggestionService.Normalize(input));
    }

    [Fact]
    public void Normalize_NullDescription_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ClassificationSuggestionService.Normalize(null!));
    }
}
