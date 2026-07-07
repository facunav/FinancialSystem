namespace FinancialSystem.Application.Review.Queries;

/// <summary>Pide las sugerencias de revisión (Matched/Unmatched/Suspicious) de un período.</summary>
public sealed record GetUnclassifiedMovementsQuery(DateOnly From, DateOnly To);
