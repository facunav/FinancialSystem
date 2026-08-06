namespace FinancialSystem.Application.Counterparties.Commands;

public sealed record UpdateCounterpartyResult(bool IsSuccess, CounterpartySummary? Counterparty)
{
    public static UpdateCounterpartyResult NotFound() => new(false, null);

    public static UpdateCounterpartyResult Success(CounterpartySummary counterparty) => new(true, counterparty);
}
