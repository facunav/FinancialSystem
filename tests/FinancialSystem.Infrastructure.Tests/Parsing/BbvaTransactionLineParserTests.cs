using FinancialSystem.Application.Parsing.Bbva;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Parsing;

/// <summary>
/// Cubre el bug confirmado donde una transacción podía quedar persistida con
/// Currency="USD" y Amount en pesos: CurrencyDetector.TryExtractUsdAmount tenía un
/// segundo intento que, ante 'USD' sin un monto pegado, tomaba el primer monto
/// disponible en el resto de la línea — en la práctica, el total en pesos al final —
/// y lo devolvía como si fuera el monto en dólares. El fix elimina ese segundo
/// intento y, si no hay un monto en USD confiable, hace que BbvaTransactionLineParser
/// registre la transacción en ARS (moneda e importe cambian juntos, nunca uno solo).
/// </summary>
public class BbvaTransactionLineParserTests
{
    private static BbvaTransactionLineParser CreateParser() =>
        new(NullLogger<BbvaTransactionLineParser>.Instance);

    [Fact]
    public void ParseTransaction_LineaSinMarcaUsd_QuedaEnArs()
    {
        var parser = CreateParser();

        var result = parser.ParseTransaction(
            "28-Mar-26 DLO*PEDIDOSYA MCDONALD     003842    32.725,00");

        Assert.True(result.Success);
        Assert.Equal("ARS", result.Value!.Currency);
        Assert.Equal(32725.00m, result.Value.Amount);
    }

    [Fact]
    public void ParseTransaction_UsdConMontoPegado_UsaElMontoEnDolares()
    {
        var parser = CreateParser();

        var result = parser.ParseTransaction(
            "28-Mar-26 PLAYSTATION USD 4,99       886221    4,99");

        Assert.True(result.Success);
        Assert.Equal("USD", result.Value!.Currency);
        Assert.Equal(4.99m, result.Value.Amount);
    }

    [Fact]
    public void ParseTransaction_UsdSinMontoPegado_NoMezclaMonedaYMontoDistintos()
    {
        // Caso del bug: "USD" aparece como marca suelta, sin un monto propio pegado
        // al lado — el único monto disponible en el resto de la línea es el total en
        // pesos. Antes del fix, esto producía Currency="USD" con ese total en pesos
        // como Amount. Ahora debe quedar consistente: ARS con el monto en pesos.
        var parser = CreateParser();

        var result = parser.ParseTransaction(
            "28-Mar-26 SPOTIFY SUSCRIPCION USD    123456    9.500,00");

        Assert.True(result.Success);
        Assert.Equal("ARS", result.Value!.Currency);
        Assert.Equal(9500.00m, result.Value.Amount);
    }
}
