using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Domain.Dedupe;

/// <summary>
/// Snapshot exacto de un <see cref="MovementIdentityLink"/> al momento de ser revertido
/// -- DEDUPE-010. Una fila por cada miembro que tenía el grupo (Pendiente, Liquidado,
/// CarryForward), copiando TODOS sus campos originales antes de que la fila real de
/// <c>MovementIdentityLink</c> se borre. Sin esto, borrar el link pierde para siempre
/// qué representación física (SourceEntityType+SourceId) formaba parte del grupo, con
/// qué evidencia y quién/cuándo lo había aplicado -- exactamente lo que hace falta para
/// responder "qué existió" después de un rollback.
///
/// Sin JSON ni blobs a propósito: mismo estilo relacional que ya usa el resto del
/// proyecto (ver InvestigationFinding/InvestigationReference) -- una fila por miembro,
/// tipada, consultable con SQL normal.
/// </summary>
public class MovementIdentityLinkRollbackMember
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid RollbackId { get; set; }
    public MovementIdentityLinkRollback Rollback { get; set; } = null!;

    /// <summary>Qué tabla contenía la representación física -- copiado del link original.</summary>
    public SourceEntityType SourceEntityType { get; set; }

    /// <summary>Id de la representación física en su tabla origen -- copiado del link original.</summary>
    public Guid SourceId { get; set; }

    public IdentityRole Role { get; set; }
    public IdentityClassification Classification { get; set; }
    public string Evidence { get; set; } = string.Empty;

    /// <summary>CreatedAtUtc del MovementIdentityLink original -- cuándo se había aplicado, no cuándo se revirtió.</summary>
    public DateTime OriginalCreatedAtUtc { get; set; }

    /// <summary>CreatedBy del MovementIdentityLink original -- quién/qué lo había aplicado.</summary>
    public string OriginalCreatedBy { get; set; } = string.Empty;
}
