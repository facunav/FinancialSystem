using FinancialSystem.Domain.Dedupe;

namespace FinancialSystem.Application.Dedupe;

/// <summary>
/// Segunda capa de detección de identidad entre representaciones físicas de
/// <c>BankStatement</c> (pendiente ↔ liquidado, carry-forward), según la especificación
/// DEDUPE-003-CONV. Complementa, no reemplaza, la idempotencia por <c>ExternalId</c>
/// existente (primera capa, ver <c>BbvaBankStatementImporter</c>).
///
/// SOLO LECTURA POR DEFECTO: <see cref="PreviewAsync"/> nunca persiste nada — evalúa
/// candidatos y devuelve el resultado para revisión humana. Solo <see cref="ApplyAsync"/>,
/// invocado explícitamente con resultados ya revisados, escribe <see cref="MovementIdentityLink"/>.
/// </summary>
public interface IDedupeEngine
{
    /// <summary>
    /// Evalúa candidatos de identidad sin persistir nada. Si <paramref name="focusBankStatementIds"/>
    /// es null, evalúa toda la cuenta (uso: backfill/preview histórico). Si se pasa una
    /// lista de Ids, solo esas filas actúan como "pendiente" candidato — el resto de la
    /// cuenta se sigue usando como universo de comparación para las señales K/M/L (uso:
    /// import en vivo, evaluar solo lo recién insertado contra todo lo ya existente).
    /// Nunca incluye resultados DESCARTADO en la lista devuelta (se filtran acá mismo,
    /// no son candidatos a mostrar en una revisión).
    /// </summary>
    Task<IReadOnlyList<DedupeCandidateResult>> PreviewAsync(
        IReadOnlyList<Guid>? focusBankStatementIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revalida cada resultado contra el estado ACTUAL de la cuenta (DEDUPE-005,
    /// invariante I4) — nunca confía ciegamente en que un <see cref="DedupeCandidateResult"/>
    /// generado por <see cref="PreviewAsync"/>, posiblemente horas o días antes, siga
    /// siendo válido. La revalidación reutiliza <c>Evaluate</c> (la misma fuente de verdad
    /// que <see cref="PreviewAsync"/>, acotada por resultado vía <c>focusIds</c>) — nunca
    /// reimplementa ninguna señal de clasificación acá.
    ///
    /// Persiste un <see cref="MovementIdentityLink"/> por cada representación de cada
    /// resultado cuya <see cref="DedupeCandidateResult.Classification"/> sea
    /// <see cref="IdentityClassification.Fuerte"/> Y que, tras la revalidación, siga
    /// siéndolo con exactamente los mismos miembros — cualquier otro resultado de la
    /// lista se ignora (nunca se persiste Posible/Indeterminado/Descartado). Idempotente:
    /// si alguna representación ya tiene un link, ese resultado se saltea sin error ni
    /// duplicado.
    ///
    /// Cada resultado se persiste de forma AISLADA (un <c>SaveChangesAsync</c> por grupo,
    /// no uno para todo el batch) — un conflicto real de concurrencia en un resultado
    /// (violación del índice único <c>(SourceEntityType, SourceId)</c>) no impide que los
    /// demás resultados sanos del mismo batch se apliquen. Devuelve cuántos grupos de
    /// identidad nuevos se crearon y el detalle de cada resultado que no se pudo aplicar
    /// (ya aplicado / falló la revalidación / conflicto de concurrencia / otro error).
    /// </summary>
    Task<ApplyOutcome> ApplyAsync(
        IReadOnlyList<DedupeCandidateResult> results,
        string createdBy,
        CancellationToken cancellationToken = default);
}

/// <summary>Resultado de evaluar un candidato pendiente↔liquidado — DTO de solo lectura, nunca una entidad persistida.</summary>
public sealed record DedupeCandidateResult(
    Guid PendienteId,
    string PendienteConcept,
    DateTime PendienteDate,
    decimal PendienteAmount,
    string? PendienteSourceFile,
    Guid LiquidadoId,
    string LiquidadoConcept,
    DateTime LiquidadoDate,
    decimal LiquidadoAmount,
    string? LiquidadoSourceFile,
    IdentityClassification Classification,
    string Evidence,
    IReadOnlyList<Guid> CarryForwardMemberIds);

/// <summary>
/// Resultado de <see cref="IDedupeEngine.ApplyAsync"/> (DEDUPE-005, invariante I6 —
/// "reportar conflicto, no sobrescribir, no solo contar"). <see cref="GroupsCreated"/> es
/// exactamente el mismo número que antes devolvía el <c>int</c> plano; <see cref="Skipped"/>
/// es lo nuevo — el detalle de cada resultado FUERTE que no se persistió y por qué.
/// </summary>
public sealed record ApplyOutcome(int GroupsCreated, IReadOnlyList<ApplySkip> Skipped);

/// <summary>Un resultado FUERTE recibido por <c>ApplyAsync</c> que no se persistió, y el motivo exacto.</summary>
public sealed record ApplySkip(
    Guid PendienteId,
    Guid LiquidadoId,
    ApplySkipReason Reason,
    string Detail);

/// <summary>Motivo por el que <c>ApplyAsync</c> no persistió un resultado FUERTE recibido.</summary>
public enum ApplySkipReason
{
    /// <summary>Alguna de las representaciones físicas ya tenía un MovementIdentityLink antes de esta corrida.</summary>
    YaAplicado,

    /// <summary>
    /// Al revalidar contra el estado actual de la cuenta (no el snapshot de Preview), este
    /// resultado ya no se sostiene: el movimiento desapareció, cambiaron sus datos, o la
    /// clasificación ya no es FUERTE con exactamente los mismos miembros.
    /// </summary>
    RevalidacionFallida,

    /// <summary>
    /// Otra ejecución concurrente de ApplyAsync ya linkeó alguno de estos movimientos entre
    /// el momento en que se leyó el estado de "ya aplicado" y el momento en que se intentó
    /// persistir — el índice único de Postgres rechazó el insert (violación real, no supuesta).
    /// </summary>
    ConflictoDeConcurrencia,

    /// <summary>SaveChangesAsync falló por una razón distinta a un conflicto de unicidad conocido.</summary>
    ErrorInesperado,
}
