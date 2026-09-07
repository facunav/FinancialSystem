namespace FinancialSystem.Application.Review.Commands;

public enum ClassifyMovementFailureReason
{
    /// <summary>No existe un registro con ese SourceEntityType+SourceId.</summary>
    SourceNotFound,

    /// <summary>CategoryId no corresponde a ninguna categoría existente.</summary>
    CategoryNotFound,

    /// <summary>CounterpartyId (si se informó) no corresponde a ninguna contraparte existente.</summary>
    CounterpartyNotFound,

    /// <summary>
    /// El movimiento ya tiene un ClassifiedMovementItem, pero como parte de un grupo de
    /// más de un item — no se puede reclasificar individualmente sin decidir qué pasa
    /// con el resto del grupo. PR-L4: ConfirmMatchCommand, que era el único que creaba
    /// estos grupos, se retiró — esta protección se mantiene igual, porque grupos
    /// históricos de antes de ese PR siguen existiendo y siguen siendo válidos.
    /// </summary>
    AlreadyPartOfMatchGroup,

    /// <summary>
    /// DEDUPE-017: el SourceId no tiene item propio todavía, pero pertenece a un
    /// IdentityGroupId (MovementIdentityLink) cuyo otro miembro físico ya tiene un
    /// ClassifiedMovementItem -- ambas filas físicas son el mismo evento económico
    /// real (reexportación/duplicado detectado por DedupeEngine), así que clasificar
    /// esta también crearía un ClassifiedMovement duplicado. No se fusiona
    /// automáticamente ni se modifica la clasificación existente del otro miembro.
    /// </summary>
    PartOfAlreadyClassifiedIdentityGroup,
}

/// <summary>Resultado de <see cref="ClassifyMovementCommand"/>: éxito con el id creado, o motivo de fallo.</summary>
public sealed record ClassifyMovementResult(Guid? ClassifiedMovementId, ClassifyMovementFailureReason? FailureReason)
{
    public bool IsSuccess => FailureReason is null;

    public static ClassifyMovementResult Success(Guid classifiedMovementId) => new(classifiedMovementId, null);

    public static ClassifyMovementResult Failure(ClassifyMovementFailureReason reason) => new(null, reason);
}
