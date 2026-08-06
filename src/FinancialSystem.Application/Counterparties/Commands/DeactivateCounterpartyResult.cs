namespace FinancialSystem.Application.Counterparties.Commands;

public enum DeactivateCounterpartyOutcome
{
    Deactivated,
    AlreadyDeactivated,
}

public sealed record DeactivateCounterpartyResult(
    bool IsSuccess,
    DeactivateCounterpartyOutcome? Outcome,
    string? Message)
{
    public static DeactivateCounterpartyResult NotFound() => new(false, null, null);

    public static DeactivateCounterpartyResult Success(DeactivateCounterpartyOutcome outcome, string message) =>
        new(true, outcome, message);
}
