namespace FinancialSystem.Application.Review.Commands;

/// <summary>Restaura (IsDiscarded=false) los LegacyImportedExpense indicados.</summary>
public sealed record RestoreLegacyCandidatesCommand(IReadOnlyList<Guid> Ids);
