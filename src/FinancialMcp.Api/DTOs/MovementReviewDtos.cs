using System.Text.Json.Serialization;
using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Api.DTOs;

// PR-L4: hasta acá este archivo también tenía FinancialMovementDto, MatchScoreDto,
// RuleContributionDto, MatchedPairDto, UnmatchedMovementDto, SuspiciousGroupDto,
// ReviewSummaryDto, ReviewResultDto (para GET /unclassified) y los DTOs de
// confirm-match/discard-candidates/restore-candidates — el backend de matching
// contra movimientos legacy que sostenía group-reconciliation.html. Ese mecanismo
// se retiró completo junto con la pantalla. Solo queda lo que /classify necesita.

// ── POST /api/movement-review/classify ───────────────────────────────────────

// EffectiveDate es opcional y por lo tanto no rompe clientes existentes que no lo
// envían (deserializa a null). Solo tiene efecto al reclasificar — ver
// ClassifyMovementCommand/ClassifyMovementHandler.
public sealed record ClassifyMovementRequest(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] SourceEntityType SourceEntityType,
    Guid SourceId,
    Guid CategoryId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] MovementType MovementType,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] FinancialImpact FinancialImpact,
    Guid? CounterpartyId,
    string? Comment,
    DateTime? EffectiveDate = null);

public sealed record ClassifyMovementResponseDto(Guid ClassifiedMovementId, string Status);

// ── GET /api/movement-review/effective-date-suggestion ────────────────────────
//
// V1 mínima: sugiere EffectiveDate = primer día del mes siguiente cuando existen
// al menos 2 correcciones manuales previas de la misma Counterparty (Income) ya
// corridas al mes siguiente. Nunca se aplica sola -- solo prellena el campo que
// ya existe en movements.html, el usuario siempre confirma o cambia el valor.
public sealed record EffectiveDateSuggestionResponse(bool HasSuggestion, DateTime? SuggestedEffectiveDate);
