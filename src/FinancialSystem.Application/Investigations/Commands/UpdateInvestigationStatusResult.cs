namespace FinancialSystem.Application.Investigations.Commands;

public enum UpdateInvestigationStatusFailureReason
{
    /// <summary>No existe una Investigation con ese Id.</summary>
    InvestigationNotFound,

    /// <summary>Status = Resolved pero Conclusion vino vacía o nula.</summary>
    ConclusionRequired,
}

/// <summary>Resultado de <see cref="UpdateInvestigationStatusCommand"/>: éxito con UpdatedAt, o motivo de fallo.</summary>
public sealed record UpdateInvestigationStatusResult(
    bool IsSuccess, DateTime? UpdatedAt, UpdateInvestigationStatusFailureReason? FailureReason)
{
    public static UpdateInvestigationStatusResult Success(DateTime updatedAt) => new(true, updatedAt, null);

    public static UpdateInvestigationStatusResult Failure(UpdateInvestigationStatusFailureReason reason) =>
        new(false, null, reason);
}
