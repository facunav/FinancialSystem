namespace FinancialSystem.Application.Investigations.Commands;

/// <summary>
/// Resultado de <see cref="AddInvestigationFindingCommand"/>: éxito con el hallazgo
/// creado, o investigación inexistente.
/// </summary>
public sealed record AddInvestigationFindingResult(bool InvestigationFound, Guid? FindingId, DateTime? CreatedAt)
{
    public bool IsSuccess => InvestigationFound;

    public static AddInvestigationFindingResult InvestigationNotFound() => new(false, null, null);

    public static AddInvestigationFindingResult Success(Guid findingId, DateTime createdAt) =>
        new(true, findingId, createdAt);
}
