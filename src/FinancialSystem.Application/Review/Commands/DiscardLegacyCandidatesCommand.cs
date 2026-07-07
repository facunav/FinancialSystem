namespace FinancialSystem.Application.Review.Commands;

/// <summary>Marca como descartados (IsDiscarded=true) los LegacyImportedExpense indicados.</summary>
public sealed record DiscardLegacyCandidatesCommand(IReadOnlyList<Guid> Ids);
