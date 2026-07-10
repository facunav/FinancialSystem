namespace FinancialSystem.Application.Review.Commands;

public enum ConfirmMatchFailureReason
{
    /// <summary>Items vacío.</summary>
    EmptyItems,

    /// <summary>Ningún item con Role=Reference.</summary>
    MissingReference,

    /// <summary>Ningún item con Role=Candidate.</summary>
    MissingCandidate,

    /// <summary>El mismo SourceEntityType+SourceId aparece más de una vez en Items.</summary>
    DuplicateItem,

    /// <summary>
    /// El Role de un item no corresponde a su SourceEntityType (BankStatement/Transaction
    /// deben ser Reference; LegacyImport debe ser Candidate).
    /// </summary>
    RoleSourceMismatch,

    /// <summary>No existe un registro con ese SourceEntityType+SourceId.</summary>
    SourceNotFound,

    /// <summary>CategoryId no corresponde a ninguna categoría existente.</summary>
    CategoryNotFound,

    /// <summary>CounterpartyId (si se informó) no corresponde a ninguna contraparte existente.</summary>
    CounterpartyNotFound,

    /// <summary>
    /// Alguno de los items ya tiene un ClassifiedMovementItem (de una clasificación manual
    /// o de otro match confirmado previo) — confirmar igual duplicaría el movimiento en las
    /// métricas, ya que no hay índice único sobre (SourceEntityType, SourceId) a nivel de base.
    /// </summary>
    SourceAlreadyClassified,
}

/// <summary>Resultado de <see cref="ConfirmMatchCommand"/>: éxito con el id creado, o motivo de fallo.</summary>
public sealed record ConfirmMatchResult(
    Guid? ClassifiedMovementId, ConfirmMatchFailureReason? FailureReason, string? FailureDetail)
{
    public bool IsSuccess => FailureReason is null;

    public static ConfirmMatchResult Success(Guid classifiedMovementId) => new(classifiedMovementId, null, null);

    public static ConfirmMatchResult Failure(ConfirmMatchFailureReason reason, string? detail = null) =>
        new(null, reason, detail);
}
