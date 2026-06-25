using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Reconciliation.Commands
{
public sealed record ReviewMovementCommand(
    SourceEntityType SourceEntityType,
    Guid SourceId,
    ReviewReason Reason,
    Guid CategoryId,
    FinancialImpact FinancialImpact,
    string? Notes);
 
public sealed record ReviewMovementResult(bool Success, Guid? ExpenseId = null, string? Error = null)
{
    public static ReviewMovementResult Ok(Guid id) => new(true, id);
    public static ReviewMovementResult Failure(string error) => new(false, null, error);
}
 
internal sealed record SourceMovementData(
    DateTime Date,
    string Description,
    decimal Amount,
    string Currency,
    string? SourceFile);
 
public sealed class ReviewMovementHandler
{
    private readonly IProcessedExpenseRepository _repository;
    private readonly IApplicationDbContext _db;
 
    public ReviewMovementHandler(
        IProcessedExpenseRepository repository,
        IApplicationDbContext db)
    {
        _repository = repository;
        _db = db;
    }
 
    public async Task<ReviewMovementResult> Handle(ReviewMovementCommand cmd, CancellationToken ct)
    {
        if (cmd.SourceId == Guid.Empty)
            return ReviewMovementResult.Failure("SourceId es requerido");
        if (cmd.CategoryId == Guid.Empty)
            return ReviewMovementResult.Failure("CategoryId es requerido");
 
        var sourceData = await LoadSourceDataAsync(cmd.SourceEntityType, cmd.SourceId, ct);
        if (sourceData is null)
            return ReviewMovementResult.Failure(
                $"No se encontró el movimiento {cmd.SourceEntityType}:{cmd.SourceId}");
 
        var alreadyProcessed = await _repository.GetAlreadyProcessedSourceIdsAsync(
            cmd.SourceEntityType, [cmd.SourceId], ct);
        if (alreadyProcessed.Count > 0)
            return ReviewMovementResult.Failure("El movimiento ya fue procesado previamente");
 
        var now = DateTime.UtcNow;
 
        var expense = new ProcessedExpense
        {
            EffectiveDate = sourceData.Date,
            TotalAmount = Math.Abs(sourceData.Amount),
            Currency = sourceData.Currency,
            Description = sourceData.Description,
            CategoryId = cmd.CategoryId,
            FinancialImpact = cmd.FinancialImpact,
            Status = ExpenseStatus.Reviewed,
            ProcessingSource = ProcessingSource.ManualReview,
            MatchScore = null,
            AmountDelta = null,
            CreatedAt = now,
            ProcessedAt = now,
            ProcessedBy = null,
            ReviewReason = cmd.Reason,
            ReviewNotes = cmd.Notes,
        };
 
        expense.Items.Add(new ProcessedExpenseItem
        {
            SourceEntityType = cmd.SourceEntityType,
            SourceId = cmd.SourceId,
            Role = ReconciliationItemRole.Reference,
            OriginalAmount = sourceData.Amount,
            OriginalDate = sourceData.Date,
            OriginalDescription = sourceData.Description,
            OriginalCurrency = sourceData.Currency,
            OriginalSourceFile = sourceData.SourceFile,
        });
 
        await _repository.SaveAsync(expense, ct);
        return ReviewMovementResult.Ok(expense.Id);
    }
 
    private async Task<SourceMovementData?> LoadSourceDataAsync(
        SourceEntityType sourceType, Guid sourceId, CancellationToken ct) =>
        sourceType switch
        {
            SourceEntityType.Transaction => await LoadFromTransactionAsync(sourceId, ct),
            SourceEntityType.BankStatement => await LoadFromBankStatementAsync(sourceId, ct),
            SourceEntityType.ManualExpense => await LoadFromManualExpenseAsync(sourceId, ct),
            _ => null,
        };
 
    private async Task<SourceMovementData?> LoadFromTransactionAsync(Guid id, CancellationToken ct)
    {
        var tx = await _db.Transactions.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new { t.Date, t.Description, t.Amount, t.Currency, t.SourceFile })
            .FirstOrDefaultAsync(ct);
        if (tx is null) return null;
        return new SourceMovementData(tx.Date, tx.Description, tx.Amount, tx.Currency, tx.SourceFile);
    }
 
    private async Task<SourceMovementData?> LoadFromBankStatementAsync(Guid id, CancellationToken ct)
    {
        var bs = await _db.BankStatements.AsNoTracking()
            .Where(b => b.Id == id)
            .Select(b => new { b.Date, b.Concept, b.Detail, b.Amount, b.Currency, b.SourceFile })
            .FirstOrDefaultAsync(ct);
        if (bs is null) return null;
        var description = !string.IsNullOrWhiteSpace(bs.Detail)
            ? $"{bs.Concept} {bs.Detail}"
            : bs.Concept;
        return new SourceMovementData(bs.Date, description, bs.Amount, bs.Currency, bs.SourceFile);
    }
 
    private async Task<SourceMovementData?> LoadFromManualExpenseAsync(Guid id, CancellationToken ct)
    {
        var me = await _db.ManualExpenses.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new { m.Date, m.Description, m.Notes, m.Amount, m.Currency, m.SourceFile })
            .FirstOrDefaultAsync(ct);
        if (me is null) return null;
        var description = string.IsNullOrWhiteSpace(me.Notes)
            ? me.Description
            : $"{me.Description} {me.Notes}";
        return new SourceMovementData(me.Date, description, me.Amount, me.Currency, me.SourceFile);
    }
}
}