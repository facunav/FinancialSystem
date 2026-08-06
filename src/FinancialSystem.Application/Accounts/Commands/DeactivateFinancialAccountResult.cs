namespace FinancialSystem.Application.Accounts.Commands;

public enum DeactivateFinancialAccountOutcome
{
    Deactivated,
    AlreadyDeactivated,
}

public sealed record DeactivateFinancialAccountResult(
    bool IsSuccess,
    DeactivateFinancialAccountOutcome? Outcome,
    string? Message)
{
    public static DeactivateFinancialAccountResult NotFound() => new(false, null, null);

    public static DeactivateFinancialAccountResult Success(DeactivateFinancialAccountOutcome outcome, string message) =>
        new(true, outcome, message);
}
