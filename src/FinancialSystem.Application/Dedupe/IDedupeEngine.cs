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
    /// Persiste un <see cref="MovementIdentityLink"/> por cada representación de cada
    /// resultado cuya <see cref="DedupeCandidateResult.Classification"/> sea
    /// <see cref="IdentityClassification.Fuerte"/> — cualquier otro resultado de la lista
    /// se ignora silenciosamente (nunca se persiste Posible/Indeterminado/Descartado).
    /// Idempotente: si alguna de las representaciones ya tiene un link (índice único
    /// SourceEntityType+SourceId), ese resultado completo se saltea sin error ni
    /// duplicado. Devuelve cuántos grupos de identidad nuevos se crearon.
    /// </summary>
    Task<int> ApplyAsync(
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
