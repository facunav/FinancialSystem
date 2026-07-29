namespace FinancialSystem.Application.Investigations.Commands;

/// <summary>
/// Registra un hallazgo (InvestigationFinding) en una Investigation existente — ver
/// ADR-007 §3 ("Hallazgo"). No modifica Status ni References.
/// </summary>
public sealed record AddInvestigationFindingCommand(Guid InvestigationId, string Text);
