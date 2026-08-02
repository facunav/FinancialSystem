namespace FinancialSystem.Application.Planning.Commands;

/// <summary>Resultado de <see cref="UpdateExpectedIncomeCommand"/>.</summary>
public sealed record UpdateExpectedIncomeResult(bool IsSuccess)
{
    public static UpdateExpectedIncomeResult NotFound() => new(false);

    public static UpdateExpectedIncomeResult Success() => new(true);
}
