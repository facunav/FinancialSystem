using FinancialSystem.Application.Reconciliation;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Reconciliation;

namespace FinancialSystem.Api.DTOs;

// ── GET /api/reconciliation/suggestions ──────────────────────────────────────

public sealed record SuggestionsResponse(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    SuggestionsSummaryDto Summary,
    IReadOnlyList<MatchedPairDto> AutoConfirmable,
    IReadOnlyList<MatchedPairDto> NeedsReview,
    IReadOnlyList<UnmatchedDto> Unmatched)
{
    public static SuggestionsResponse FromSuggestions(ReconciliationSuggestions s) => new(
        s.PeriodStart, s.PeriodEnd,
        SuggestionsSummaryDto.From(s),
        s.AutoConfirmable.Select(MatchedPairDto.From).ToList(),
        s.NeedsReview.Select(MatchedPairDto.From).ToList(),
        s.Unmatched.Select(UnmatchedDto.From).ToList());
}

public sealed record SuggestionsSummaryDto(
    int AutoConfirmableCount, int NeedsReviewCount, int UnmatchedCount,
    int TotalReferenceMovements, int TotalCandidateMovements, long ElapsedMs)
{
    public static SuggestionsSummaryDto From(ReconciliationSuggestions s) => new(
        s.AutoConfirmable.Count, s.NeedsReview.Count, s.Unmatched.Count,
        s.Summary.TotalReferenceMovements, s.Summary.TotalCandidateMovements,
        (long)s.Elapsed.TotalMilliseconds);
}

public sealed record MatchedPairDto(
    MovementDto Reference, MovementDto Candidate,
    MatchScoreDto Score, string Confidence, decimal AmountDelta, int DateDeltaDays)
{
    public static MatchedPairDto From(MatchedPair p) => new(
        MovementDto.From(p.Reference), MovementDto.From(p.Candidate),
        MatchScoreDto.From(p.Score), p.Confidence.ToString(), p.AmountDelta, p.DateDeltaDays);
}

public sealed record MovementDto(Guid Id, DateTime Date, string Description, decimal Amount, string Currency, string Source)
{
    public static MovementDto From(FinancialMovement m) =>
        new(m.Id, m.Date, m.Description, m.Amount, m.Currency, m.Source.ToString());
}

public sealed record MatchScoreDto(double Total, double Amount, double Date, double Description, double PaymentMethod)
{
    public static MatchScoreDto From(MatchScore s) =>
        new(s.Total, s.AmountScore, s.DateScore, s.DescriptionScore, s.PaymentMethodScore);
}

public sealed record UnmatchedDto(MovementDto Movement, string Reason)
{
    public static UnmatchedDto From(UnmatchedMovement u) =>
        new(MovementDto.From(u.Movement), u.Reason.ToString());
}

// ── POST /api/reconciliation/confirm ─────────────────────────────────────────

public sealed record ConfirmRequest(
    string ConfirmedBy,
    Guid CategoryId,
    FinancialImpact FinancialImpact,
    IReadOnlyList<ConfirmPairDto> Pairs);

public sealed record ConfirmPairDto(
    Guid ReferenceId, string ReferenceSource,
    Guid CandidateId, string CandidateSource,
    double? OriginalScore = null, string? OriginalConfidence = null,
    ProcessingSource ProcessingSource = ProcessingSource.ManualMatch);

public sealed record ConfirmResponse(int Succeeded, int Failed, IReadOnlyList<ConfirmPairResultDto> Results);

public sealed record ConfirmPairResultDto(
    Guid ReferenceId, Guid CandidateId, bool Success,
    Guid? ExpenseId = null, string? Error = null);

// ── GET /api/reconciliation/processed ────────────────────────────────────────

public sealed record ProcessedExpensesResponse(
    DateTime From, DateTime To, int TotalCount, decimal TotalAmount, string Currency,
    IReadOnlyList<ProcessedExpenseDto> Items)
{
    public static ProcessedExpensesResponse FromExpenses(
        DateTime from, DateTime to, IReadOnlyList<ProcessedExpense> expenses)
    {
        var dtos = expenses.Select(ProcessedExpenseDto.From).ToList();
        return new(from, to, dtos.Count, dtos.Sum(d => d.TotalAmount),
            dtos.Count > 0 ? dtos[0].Currency : "ARS", dtos);
    }
}

public sealed record ProcessedExpenseDto(
    Guid Id, DateTime EffectiveDate, string Description, decimal TotalAmount, string Currency,
    string CategoryName, string FinancialImpact, string Status, string ProcessingSource,
    DateTime ProcessedAt, string? ProcessedBy, double? MatchScore,
    IReadOnlyList<ProcessedItemDto> ReferenceItems, IReadOnlyList<ProcessedItemDto> CandidateItems)
{
    public static ProcessedExpenseDto From(ProcessedExpense e) => new(
        e.Id, e.EffectiveDate, e.Description, e.TotalAmount, e.Currency,
        e.Category?.DisplayName ?? string.Empty,
        e.FinancialImpact.ToString(), e.Status.ToString(), e.ProcessingSource.ToString(),
        e.ProcessedAt, e.ProcessedBy, e.MatchScore,
        e.Items.Where(i => i.Role == ReconciliationItemRole.Reference).Select(ProcessedItemDto.From).ToList(),
        e.Items.Where(i => i.Role == ReconciliationItemRole.Candidate).Select(ProcessedItemDto.From).ToList());
}

public sealed record ProcessedItemDto(
    string SourceType, Guid SourceId, decimal OriginalAmount,
    DateTime OriginalDate, string OriginalDescription, string OriginalCurrency)
{
    public static ProcessedItemDto From(Domain.Reconciliation.ProcessedExpenseItem i) => new(
        i.SourceEntityType.ToString(), i.SourceId, i.OriginalAmount,
        i.OriginalDate, i.OriginalDescription, i.OriginalCurrency);
}

// ── GET /api/reconciliation/unmatched-movements ───────────────────────────────

public sealed record UnmatchedMovementsResponse(
    DateOnly PeriodStart, DateOnly PeriodEnd,
    IReadOnlyList<UnmatchedMovementDto> References,
    IReadOnlyList<UnmatchedMovementDto> Candidates);

public sealed record UnmatchedMovementDto(
    Guid Id, string Source, DateTime Date, string Description,
    decimal Amount, string Currency, bool AlreadyProcessed);

// ── POST /api/reconciliation/confirm-group ────────────────────────────────────

public sealed record ConfirmGroupRequest(
    string ConfirmedBy, Guid CategoryId, FinancialImpact FinancialImpact,
    IReadOnlyList<MovementRefDto> ReferenceItems,
    IReadOnlyList<MovementRefDto> CandidateItems);

public sealed record MovementRefDto(Guid Id, string Source);

public sealed record ConfirmGroupResponse(
    bool Success, Guid? ExpenseId = null,
    decimal ReferenceTotal = 0, decimal CandidateTotal = 0,
    decimal AmountDelta = 0, string? Error = null);

// ── POST /api/reconciliation/review ──────────────────────────────────────────

public sealed record ReviewMovementRequest(
    SourceEntityType SourceEntityType, Guid SourceId,
    ReviewReason Reason, Guid CategoryId, FinancialImpact FinancialImpact, string? Notes);

// ── POST /api/reconciliation/discard-candidates ──────────────────────────────

/// <summary>
/// Descarta uno o más MovementExpenses del Excel sin eliminarlos físicamente.
/// El campo IsDiscarded los excluye del flujo de conciliación.
/// Se pueden restaurar en el futuro con el endpoint restore-candidates.
/// </summary>
public sealed record DiscardCandidatesRequest(IReadOnlyList<Guid> Ids);

public sealed record DiscardCandidatesResponse(int Discarded, int NotFound);

// ── POST /api/reconciliation/restore-candidates ───────────────────────────────

public sealed record RestoreCandidatesRequest(IReadOnlyList<Guid> Ids);

public sealed record RestoreCandidatesResponse(int Restored, int NotFound);

// ── GET /api/categories ───────────────────────────────────────────────────────

public sealed record CategoryDto(Guid Id, string Name, string DisplayName, int SortOrder);