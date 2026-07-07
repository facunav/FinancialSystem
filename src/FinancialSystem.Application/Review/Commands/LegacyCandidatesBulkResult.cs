namespace FinancialSystem.Application.Review.Commands;

public enum LegacyCandidatesBulkFailureReason
{
    /// <summary>Ids vacío.</summary>
    EmptyIds,
}

/// <summary>
/// Resultado de una operación bulk (discard/restore) sobre LegacyImportedExpense
/// por Id: qué ids se actualizaron y cuáles no correspondían a ningún registro.
/// </summary>
public sealed record LegacyCandidatesBulkResult(
    IReadOnlyList<Guid> UpdatedIds,
    IReadOnlyList<Guid> NotFoundIds,
    LegacyCandidatesBulkFailureReason? FailureReason)
{
    public bool IsSuccess => FailureReason is null;

    public static LegacyCandidatesBulkResult Success(
        IReadOnlyList<Guid> updatedIds, IReadOnlyList<Guid> notFoundIds) =>
        new(updatedIds, notFoundIds, null);

    public static LegacyCandidatesBulkResult Failure(LegacyCandidatesBulkFailureReason reason) =>
        new([], [], reason);
}
