using FinancialSystem.Application.Dedupe;

namespace FinancialSystem.Api.DTOs;

// ── POST /api/dedupe/apply ───────────────────────────────────────────────────

/// <summary>
/// Request de PATCH-0112: el cliente identifica los <c>BankStatement</c> físicos que
/// quiere vincular como el mismo movimiento real -- nunca envía <c>PendienteId</c>/
/// <c>LiquidadoId</c> ni ningún <c>DedupeCandidateResult</c> armado por él (esa
/// distinción de rol es interna al motor, ver <c>IsCandidatePair.roleOk</c> en
/// <c>DedupeEngine</c>, y el cliente no debe necesitar conocerla). El servidor
/// reconstruye el candidato entero llamando <c>PreviewAsync(focusBankStatementIds:
/// BankStatementIds)</c> con esta misma lista completa, y solo aplica si encuentra, de
/// forma inequívoca, un candidato FUERTE cuyos miembros coinciden exactamente con este
/// conjunto (ver <see cref="DedupeEndpoints"/>).
/// </summary>
public sealed record DedupeApplyRequest(IReadOnlyList<Guid>? BankStatementIds);

/// <summary>
/// Traducción 1 a 1 de <see cref="ApplySkip"/> -- <see cref="Reason"/> es el nombre
/// exacto de <see cref="ApplySkipReason"/> y <see cref="Detail"/> es siempre el texto
/// real que devolvió <c>ApplyAsync</c>, nunca reinterpretado. En particular,
/// <c>YaAplicado</c> hoy representa dos situaciones distintas del modelo actual (ambos
/// miembros ya vinculados al mismo grupo -- nada que hacer -- o un miembro ya vinculado
/// a OTRO grupo -- conflicto real) sin distinguirlas estructuralmente (ver revisión
/// pre-implementación de PATCH-0112); este DTO no inventa esa distinción, expone el
/// <see cref="Detail"/> real para que quien consuma la respuesta pueda investigar.
/// </summary>
public sealed record DedupeApplySkipDto(Guid PendienteId, Guid LiquidadoId, string Reason, string Detail)
{
    public static DedupeApplySkipDto Create(ApplySkip skip) =>
        new(skip.PendienteId, skip.LiquidadoId, skip.Reason.ToString(), skip.Detail);
}

/// <summary>Traducción directa de <see cref="ApplyOutcome"/> -- no reinterpreta el resultado real de <c>ApplyAsync</c>.</summary>
public sealed record DedupeApplyResponseDto(int GroupsCreated, IReadOnlyList<DedupeApplySkipDto> Skipped)
{
    public static DedupeApplyResponseDto Create(ApplyOutcome outcome) => new(
        outcome.GroupsCreated,
        outcome.Skipped.Select(DedupeApplySkipDto.Create).ToList());
}

// ── POST /api/dedupe/rollback ────────────────────────────────────────────────

/// <summary>
/// Request de DEDUPE-010: el cliente identifica el grupo completo por
/// <see cref="IdentityGroupId"/> -- nunca un <c>SourceId</c> individual, nunca puede
/// elegir arbitrariamente qué miembros revertir (ver
/// <see cref="DedupeEndpoints"/>). <see cref="Reason"/> es obligatorio.
/// </summary>
public sealed record DedupeRollbackRequest(Guid IdentityGroupId, string? Reason);

/// <summary>Traducción directa de <see cref="RollbackResult"/> -- no reinterpreta el resultado real del servicio.</summary>
public sealed record DedupeRollbackResponseDto(string Outcome, Guid IdentityGroupId, int MembersAffected)
{
    public static DedupeRollbackResponseDto Create(RollbackResult result) => new(
        result.Outcome.ToString(), result.IdentityGroupId, result.MembersAffected);
}
