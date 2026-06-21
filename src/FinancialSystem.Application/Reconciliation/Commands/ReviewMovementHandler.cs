using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Reconciliation;

namespace FinancialSystem.Application.Reconciliation.Commands
{
    public sealed record ReviewMovementCommand(
        SourceEntityType SourceEntityType,
        Guid SourceId,
        ReviewReason Reason,
        string? Notes);

    public sealed record ReviewMovementResult(bool Success, Guid? ExpenseId = null, string? Error = null)
    {
        public static ReviewMovementResult Ok(Guid id) => new(true, id);
        public static ReviewMovementResult Failure(string error) => new(false, null, error);
    }

    public sealed class ReviewMovementHandler
    {
        private readonly IReconciledExpenseRepository _repository;

        public ReviewMovementHandler(IReconciledExpenseRepository repository)
        {
            _repository = repository;
        }

        public async Task<ReviewMovementResult> Handle(ReviewMovementCommand cmd, CancellationToken ct)
        {
            if (cmd.SourceId == Guid.Empty)
                return ReviewMovementResult.Failure("SourceId es requerido");

            var now = DateTime.UtcNow;

            var expense = new ReconciledExpense
            {
                PeriodStart = DateOnly.FromDateTime(now),
                PeriodEnd = DateOnly.FromDateTime(now),
                EffectiveDate = now,
                TotalAmount = 0m,
                Currency = "ARS",
                Description = string.Empty,
                Status = ReconciledExpenseStatus.Reviewed,
                MatchScore = 0.0,
                MatchConfidence = "Manual Review",
                ConfirmationSource = ConfirmationSource.Manual,
                CreatedAt = now,
                // Reviewed items are not confirmed as a financial truth, so leave ConfirmedAt/ConfirmedBy null
                ConfirmedAt = null,
                ConfirmedBy = null,
                ReviewReason = cmd.Reason,
                ReviewNotes = cmd.Notes,
            };

            expense.Items.Add(new ReconciledExpenseItem
            {
                SourceEntityType = cmd.SourceEntityType,
                SourceId = cmd.SourceId,
                Role = ReconciliationItemRole.Reference,
                OriginalAmount = 0m,
                OriginalDate = now,
                OriginalDescription = string.Empty,
                OriginalCurrency = expense.Currency,
            });

            await _repository.SaveAsync(expense, ct);

            return ReviewMovementResult.Ok(expense.Id);
        }
    }
}
