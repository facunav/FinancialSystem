namespace FinancialSystem.Application.Investigations.Commands;

/// <summary>
/// Crea una Investigation nueva en estado Open. Sin referencias iniciales — ver
/// ADR-007 (Memoria del Financial MCP): esta es la Fase 3 ("tools para crear,
/// actualizar y consultar investigaciones") aplicada solo al caso de creación.
/// </summary>
public sealed record CreateInvestigationCommand(string Question, string? Tags);
