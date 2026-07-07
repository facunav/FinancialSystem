using System.Text.Json.Serialization;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;

namespace FinancialSystem.Api.DTOs;

// ── GET /api/movement-review/unclassified ────────────────────────────────────

public sealed record FinancialMovementDto(
    Guid Id,
    // Identificador técnico: el Guid real de la fila en su tabla de origen.
    // Es lo que hay que enviar como sourceId en classify/confirm-match/discard-candidates.
    Guid SourceId,
    DateTime Date,
    string Description,
    decimal Amount,
    string Currency,
    string Source,
    string Category,
    string? PaymentMethod,
    // Referencia de negocio (cupón, número de fila, etc.), solo para mostrar en UI.
    // No usar como identificador técnico — para eso está SourceId.
    string? OriginalId,
    string? SourceFile)
{
    public static FinancialMovementDto Create(FinancialMovement m) => new(
        m.Id, m.SourceId, m.Date, m.Description, m.Amount, m.Currency,
        m.Source.ToString(), m.Category.ToString(),
        m.PaymentMethod?.ToString(), m.OriginalId, m.SourceFile);
}

public sealed record MatchScoreDto(
    double AmountScore,
    double DateScore,
    double DescriptionScore,
    double PaymentMethodScore,
    double Total)
{
    public static MatchScoreDto Create(MatchScore s) => new(
        s.AmountScore, s.DateScore, s.DescriptionScore, s.PaymentMethodScore, s.Total);
}

public sealed record RuleContributionDto(string RuleName, double Score, double Weight, string? Detail)
{
    public static RuleContributionDto Create(RuleContribution c) => new(c.RuleName, c.Score, c.Weight, c.Detail);
}

public sealed record MatchedPairDto(
    FinancialMovementDto Reference,
    FinancialMovementDto Candidate,
    MatchScoreDto Score,
    string Confidence,
    IReadOnlyList<RuleContributionDto> Contributions,
    decimal AmountDelta,
    int DateDeltaDays)
{
    public static MatchedPairDto Create(MatchedPair p) => new(
        FinancialMovementDto.Create(p.Reference),
        FinancialMovementDto.Create(p.Candidate),
        MatchScoreDto.Create(p.Score),
        p.Confidence.ToString(),
        p.Contributions.Select(RuleContributionDto.Create).ToList(),
        p.AmountDelta,
        p.DateDeltaDays);
}

public sealed record UnmatchedMovementDto(
    FinancialMovementDto Movement,
    string Reason,
    IReadOnlyList<MatchedPairDto> NearMisses)
{
    public static UnmatchedMovementDto Create(UnmatchedMovement u) => new(
        FinancialMovementDto.Create(u.Movement),
        u.Reason.ToString(),
        u.NearMisses.Select(MatchedPairDto.Create).ToList());
}

public sealed record SuspiciousGroupDto(
    IReadOnlyList<FinancialMovementDto> Movements,
    string Reason,
    string Description)
{
    public static SuspiciousGroupDto Create(SuspiciousGroup g) => new(
        g.Movements.Select(FinancialMovementDto.Create).ToList(),
        g.Reason.ToString(),
        g.Description);
}

public sealed record ReviewSummaryDto(
    int TotalReferenceMovements,
    int TotalCandidateMovements,
    int HighConfidenceMatches,
    int MediumConfidenceMatches,
    int LowConfidenceMatches,
    int TotalMatched,
    int UnmatchedReference,
    int UnmatchedCandidate,
    int SuspiciousGroups,
    double MatchRate,
    decimal TotalUnmatchedAmount)
{
    public static ReviewSummaryDto Create(ReviewSummary s) => new(
        s.TotalReferenceMovements, s.TotalCandidateMovements,
        s.HighConfidenceMatches, s.MediumConfidenceMatches, s.LowConfidenceMatches,
        s.TotalMatched, s.UnmatchedReference, s.UnmatchedCandidate, s.SuspiciousGroups,
        s.MatchRate, s.TotalUnmatchedAmount);
}

public sealed record ReviewResultDto(
    string PeriodStart,
    string PeriodEnd,
    IReadOnlyList<MatchedPairDto> Matched,
    IReadOnlyList<UnmatchedMovementDto> Unmatched,
    IReadOnlyList<SuspiciousGroupDto> Suspicious,
    ReviewSummaryDto Summary,
    double ElapsedMs)
{
    public static ReviewResultDto Create(ReviewResult r) => new(
        r.PeriodStart.ToString("yyyy-MM-dd"),
        r.PeriodEnd.ToString("yyyy-MM-dd"),
        r.Matched.Select(MatchedPairDto.Create).ToList(),
        r.Unmatched.Select(UnmatchedMovementDto.Create).ToList(),
        r.Suspicious.Select(SuspiciousGroupDto.Create).ToList(),
        ReviewSummaryDto.Create(r.Summary),
        r.Elapsed.TotalMilliseconds);
}

// ── POST /api/movement-review/classify ───────────────────────────────────────

public sealed record ClassifyMovementRequest(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] SourceEntityType SourceEntityType,
    Guid SourceId,
    Guid CategoryId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] MovementType MovementType,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] FinancialImpact FinancialImpact,
    Guid? CounterpartyId,
    string? Comment);

public sealed record ClassifyMovementResponseDto(Guid ClassifiedMovementId, string Status);

// ── POST /api/movement-review/confirm-match ──────────────────────────────────

public sealed record ConfirmMatchItemRequest(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] SourceEntityType SourceEntityType,
    Guid SourceId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] MovementRole Role);

public sealed record ConfirmMatchRequest(
    IReadOnlyList<ConfirmMatchItemRequest> Items,
    Guid CategoryId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] MovementType MovementType,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] FinancialImpact FinancialImpact,
    Guid? CounterpartyId);

public sealed record ConfirmMatchResponseDto(Guid ClassifiedMovementId, string Status);

// ── POST /api/movement-review/discard-candidates y /restore-candidates ──────

public sealed record LegacyCandidatesIdsRequest(IReadOnlyList<Guid> Ids);

public sealed record LegacyCandidatesBulkResponseDto(
    IReadOnlyList<Guid> UpdatedIds,
    IReadOnlyList<Guid> NotFoundIds);
