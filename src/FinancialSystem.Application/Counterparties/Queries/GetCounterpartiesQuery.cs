namespace FinancialSystem.Application.Counterparties.Queries;

/// <summary>Misma semántica que el GET original: por defecto excluye desactivadas; Search filtra por substring de Name (case-insensitive).</summary>
public sealed record GetCounterpartiesQuery(bool IncludeDeactivated, string? Search);
