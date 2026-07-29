namespace FinancialSystem.Application.Investigations.Commands;

public enum LinkMovementToInvestigationOutcome
{
    /// <summary>Se creó una InvestigationReference nueva.</summary>
    Linked,

    /// <summary>
    /// Ya existía una InvestigationReference con ese InvestigationId+SourceEntityType+SourceId;
    /// no se creó una duplicada.
    /// </summary>
    AlreadyLinked,
}

/// <summary>
/// Resultado de <see cref="LinkMovementToInvestigationCommand"/>: éxito con el desenlace
/// (Linked/AlreadyLinked), o investigación inexistente.
/// </summary>
public sealed record LinkMovementToInvestigationResult(bool InvestigationFound, LinkMovementToInvestigationOutcome? Outcome)
{
    public bool IsSuccess => InvestigationFound;

    public static LinkMovementToInvestigationResult InvestigationNotFound() => new(false, null);

    public static LinkMovementToInvestigationResult Success(LinkMovementToInvestigationOutcome outcome) => new(true, outcome);
}
