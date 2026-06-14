using FinancialSystem.Application.Reconciliation;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Reconciliation;

namespace FinancialSystem.Api.DTOs
{
    // ════════════════════════════════════════════════════════════════
    // GET /api/reconciliation/suggestions
    // ════════════════════════════════════════════════════════════════

    public sealed record SuggestionsResponse(
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        SuggestionsSummaryDto Summary,
        IReadOnlyList<MatchedPairDto> AutoConfirmable,
        IReadOnlyList<MatchedPairDto> NeedsReview,
        IReadOnlyList<UnmatchedDto> Unmatched)
    {
        public static SuggestionsResponse FromSuggestions(ReconciliationSuggestions s) => new(
            s.PeriodStart,
            s.PeriodEnd,
            SuggestionsSummaryDto.From(s),
            s.AutoConfirmable.Select(MatchedPairDto.From).ToList(),
            s.NeedsReview.Select(MatchedPairDto.From).ToList(),
            s.Unmatched.Select(UnmatchedDto.From).ToList());
    }

    public sealed record SuggestionsSummaryDto(
        int AutoConfirmableCount,
        int NeedsReviewCount,
        int UnmatchedCount,
        int TotalReferenceMovements,
        int TotalCandidateMovements,
        long ElapsedMs)
    {
        public static SuggestionsSummaryDto From(ReconciliationSuggestions s) => new(
            s.AutoConfirmable.Count,
            s.NeedsReview.Count,
            s.Unmatched.Count,
            s.Summary.TotalReferenceMovements,
            s.Summary.TotalCandidateMovements,
            (long)s.Elapsed.TotalMilliseconds);
    }

    public sealed record MatchedPairDto(
        MovementDto Reference,
        MovementDto Candidate,
        MatchScoreDto Score,
        string Confidence,
        decimal AmountDelta,
        int DateDeltaDays)
    {
        public static MatchedPairDto From(MatchedPair p) => new(
            MovementDto.From(p.Reference),
            MovementDto.From(p.Candidate),
            MatchScoreDto.From(p.Score),
            p.Confidence.ToString(),
            p.AmountDelta,
            p.DateDeltaDays);
    }

    public sealed record MovementDto(
        Guid Id,
        DateTime Date,
        string Description,
        decimal Amount,
        string Currency,
        string Source)
    {
        public static MovementDto From(FinancialMovement m) => new(
            m.Id, m.Date, m.Description, m.Amount, m.Currency, m.Source.ToString());
    }

    public sealed record MatchScoreDto(
        double Total,
        double Amount,
        double Date,
        double Description,
        double PaymentMethod)
    {
        public static MatchScoreDto From(MatchScore s) => new(
            s.Total, s.AmountScore, s.DateScore, s.DescriptionScore, s.PaymentMethodScore);
    }

    public sealed record UnmatchedDto(
        MovementDto Movement,
        string Reason)
    {
        public static UnmatchedDto From(UnmatchedMovement u) => new(
            MovementDto.From(u.Movement),
            u.Reason.ToString());
    }

    // ════════════════════════════════════════════════════════════════
    // POST /api/reconciliation/confirm
    // ════════════════════════════════════════════════════════════════

    public sealed record ConfirmRequest(
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        string ConfirmedBy,
        IReadOnlyList<ConfirmPairDto> Pairs);

    public sealed record ConfirmPairDto(
        Guid ReferenceId,
        string ReferenceSource,      // "BankDebit" | "CreditCard"
        Guid CandidateId,
        string CandidateSource,      // "ManualDynamic" | "ManualFixed"
                                     // Campos opcionales: presentes cuando viene de una sugerencia del motor
        double? OriginalScore = null,
        string? OriginalConfidence = null,
        ConfirmationSource ConfirmationSource = ConfirmationSource.Manual);

    public sealed record ConfirmResponse(
        int Succeeded,
        int Failed,
        IReadOnlyList<ConfirmPairResultDto> Results);

    public sealed record ConfirmPairResultDto(
        Guid ReferenceId,
        Guid CandidateId,
        bool Success,
        Guid? ExpenseId = null,
        string? Error = null);

    // ════════════════════════════════════════════════════════════════
    // GET /api/reconciliation/reconciled
    // ════════════════════════════════════════════════════════════════

    public sealed record ReconciledExpensesResponse(
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        int TotalCount,
        decimal TotalAmount,
        string Currency,
        IReadOnlyList<ReconciledExpenseDto> Items)
    {
        public static ReconciledExpensesResponse From(
            DateOnly from, DateOnly to,
            IReadOnlyList<ReconciledExpense> expenses)
        {
            var dtos = expenses.Select(ReconciledExpenseDto.From).ToList();
            return new(
                from, to,
                dtos.Count,
                dtos.Sum(d => d.TotalAmount),
                dtos.Count > 0 ? dtos[0].Currency : "ARS",
                dtos);
        }
    }

    public sealed record ReconciledExpenseDto(
        Guid Id,
        DateTime EffectiveDate,
        string Description,
        decimal TotalAmount,
        string Currency,
        double MatchScore,
        string MatchConfidence,
        string ConfirmationSource,
        DateTime ConfirmedAt,
        string ConfirmedBy,
        ReconciledItemDto Reference,
        ReconciledItemDto Candidate)
    {
        public static ReconciledExpenseDto From(ReconciledExpense e)
        {
            var refItem = e.Items.First(i => i.Role == ReconciliationItemRole.Reference);
            var candItem = e.Items.First(i => i.Role == ReconciliationItemRole.Candidate);

            return new(
                e.Id,
                e.EffectiveDate,
                e.Description,
                e.TotalAmount,
                e.Currency,
                e.MatchScore,
                e.MatchConfidence,
                e.ConfirmationSource.ToString(),
                e.ConfirmedAt ?? e.CreatedAt,
                e.ConfirmedBy ?? string.Empty,
                ReconciledItemDto.From(refItem),
                ReconciledItemDto.From(candItem));
        }
    }

    public sealed record ReconciledItemDto(
        string SourceType,
        Guid SourceId,
        decimal OriginalAmount,
        DateTime OriginalDate,
        string OriginalDescription,
        string OriginalCurrency)
    {
        public static ReconciledItemDto From(ReconciledExpenseItem i) => new(
            i.SourceEntityType.ToString(),
            i.SourceId,
            i.OriginalAmount,
            i.OriginalDate,
            i.OriginalDescription,
            i.OriginalCurrency);
    }
}
