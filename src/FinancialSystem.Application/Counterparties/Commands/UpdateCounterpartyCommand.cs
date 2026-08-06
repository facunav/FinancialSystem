namespace FinancialSystem.Application.Counterparties.Commands;

public sealed record UpdateCounterpartyCommand(
    Guid CounterpartyId,
    string? Name,
    string? Notes,
    Guid? DefaultCategoryId,
    string? DefaultMovementType,
    string? DefaultFinancialImpact);
