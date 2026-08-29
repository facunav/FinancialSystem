namespace FinancialSystem.Domain.Dedupe;

/// <summary>
/// Registro de auditoría de una reversión de <see cref="MovementIdentityLink"/> --
/// DEDUPE-010. Un <c>MovementIdentityLink</c> revertido se BORRA (nunca queda una fila
/// "inactiva" en esa tabla, para no reintroducir ambigüedad sobre si un grupo sigue
/// vigente); esta entidad es lo único que preserva que ese grupo existió, quién lo
/// deshizo, cuándo y por qué -- sin esto, un <c>DELETE</c> directo sería irreversible en
/// el sentido de "no queda registro de que pasó".
///
/// UN REGISTRO POR GRUPO, NO POR MIEMBRO: <see cref="IdentityGroupId"/> tiene un índice
/// único real -- es el mecanismo de idempotencia y de concurrencia (ver
/// MovementIdentityLinkRollbackService.RollbackAsync): un segundo intento de revertir el
/// mismo grupo choca contra ese índice, nunca duplica la auditoría. El detalle de qué
/// filas físicas formaban el grupo (snapshot completo de cada
/// <c>MovementIdentityLink</c> borrado) vive en <see cref="MovementIdentityLinkRollbackMember"/>,
/// FK real hacia esta entidad -- a diferencia de <c>MovementIdentityLink</c>, que
/// deliberadamente NO tiene FK hacia <c>BankStatement</c> (referencia blanda a una tabla
/// de origen externa), acá la relación es interna al propio subsistema de auditoría de
/// Dedupe, así que una FK real con cascada es el patrón correcto (mismo criterio que
/// Investigation/InvestigationFinding).
/// </summary>
public class MovementIdentityLinkRollback
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>El grupo de identidad revertido -- índice único, ver doc-comment de la clase.</summary>
    public Guid IdentityGroupId { get; set; }

    /// <summary>Actor autenticado que ejecutó la reversión (mismo mecanismo que ApplyAsync/createdBy).</summary>
    public string RolledBackBy { get; set; } = string.Empty;

    public DateTime RolledBackAtUtc { get; set; }

    /// <summary>Motivo obligatorio de la reversión -- nunca vacío (validado antes de persistir).</summary>
    public string Reason { get; set; } = string.Empty;

    public ICollection<MovementIdentityLinkRollbackMember> Members { get; set; } = [];
}
