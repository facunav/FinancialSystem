namespace FinancialSystem.Application.Categories.Commands;

/// <summary>Desactiva una Category (borrado lógico, nunca físico).</summary>
public sealed record DeactivateCategoryCommand(Guid CategoryId);
