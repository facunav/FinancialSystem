namespace FinancialSystem.Application.Review.Commands;

public enum ClassifyMovementFailureReason
{
    /// <summary>No existe un registro con ese SourceEntityType+SourceId.</summary>
    SourceNotFound,

    /// <summary>CategoryId no corresponde a ninguna categoría existente.</summary>
    CategoryNotFound,

    /// <summary>CounterpartyId (si se informó) no corresponde a ninguna contraparte existente.</summary>
    CounterpartyNotFound,
}

/// <summary>Resultado de <see cref="ClassifyMovementCommand"/>: éxito con el id creado, o motivo de fallo.</summary>
public sealed record ClassifyMovementResult(Guid? ClassifiedMovementId, ClassifyMovementFailureReason? FailureReason)
{
    public bool IsSuccess => FailureReason is null;

    public static ClassifyMovementResult Success(Guid classifiedMovementId) => new(classifiedMovementId, null);

    public static ClassifyMovementResult Failure(ClassifyMovementFailureReason reason) => new(null, reason);
}
