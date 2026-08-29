namespace FinancialSystem.Application.Dedupe;

/// <summary>
/// DEDUPE-010: reversión auditable de un grupo completo de <c>MovementIdentityLink</c>
/// (mismo <c>IdentityGroupId</c>). Nunca opera sobre un <c>SourceId</c> suelto -- el
/// grupo entero se revierte o no se toca nada. No modifica <c>BankStatements</c>. No
/// tiene relación con <c>DedupeEngine</c>/<c>Evaluate</c>/<c>ApplyAsync</c> -- es un
/// servicio separado, deliberadamente, para no tocar el motor de detección ni el
/// contrato ya validado de PATCH-0111/0112.
/// </summary>
public interface IMovementIdentityLinkRollbackService
{
    /// <summary>
    /// Revierte el grupo <paramref name="identityGroupId"/> completo: por cada
    /// <c>MovementIdentityLink</c> que tenga ese <c>IdentityGroupId</c>, guarda un
    /// snapshot exacto en <c>MovementIdentityLinkRollbackMember</c> y borra la fila
    /// original -- todo en un único <c>SaveChangesAsync</c> (atómico). Idempotente: si
    /// el grupo ya fue revertido antes, no reintenta el borrado y devuelve
    /// <see cref="RollbackOutcome.AlreadyRolledBack"/>. Si el grupo nunca existió,
    /// devuelve <see cref="RollbackOutcome.NotFound"/>.
    /// </summary>
    Task<RollbackResult> RollbackAsync(
        Guid identityGroupId,
        string rolledBackBy,
        string reason,
        CancellationToken cancellationToken = default);
}

/// <summary>Resultado de <see cref="IMovementIdentityLinkRollbackService.RollbackAsync"/>.</summary>
public sealed record RollbackResult(RollbackOutcome Outcome, Guid IdentityGroupId, int MembersAffected);

public enum RollbackOutcome
{
    /// <summary>El grupo existía y se revirtió ahora -- MembersAffected refleja cuántos miembros tenía.</summary>
    RolledBack,

    /// <summary>Ya existía un registro de reversión para este IdentityGroupId -- no se tocó nada de nuevo.</summary>
    AlreadyRolledBack,

    /// <summary>No existe (ni existió nunca, según la auditoría) ningún grupo con ese IdentityGroupId.</summary>
    NotFound,
}
