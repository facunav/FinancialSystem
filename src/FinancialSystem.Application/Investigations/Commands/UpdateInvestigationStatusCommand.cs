using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Application.Investigations.Commands;

/// <summary>
/// Cambia el Status de una Investigation existente — ver ADR-007 §5. Conclusion solo
/// tiene efecto cuando Status = Resolved (ver UpdateInvestigationStatusHandler).
/// </summary>
public sealed record UpdateInvestigationStatusCommand(
    Guid InvestigationId,
    InvestigationStatus Status,
    string? Conclusion);
