using FinancialSystem.Domain.Dedupe;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Memory;
using FinancialSystem.Domain.Planning;
using FinancialSystem.Domain.Review;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Abstractions;

public interface IApplicationDbContext
{
    DbSet<Transaction> Transactions { get; }
    DbSet<BankStatement> BankStatements { get; }
    DbSet<Category> Categories { get; }
    DbSet<ClassifiedMovement> ClassifiedMovements { get; }
    DbSet<ClassifiedMovementItem> ClassifiedMovementItems { get; }
    DbSet<Counterparty> Counterparties { get; }
    DbSet<ImportBatch> ImportBatches { get; }
    DbSet<ImportBatchLine> ImportBatchLines { get; }
    DbSet<FinancialAccount> FinancialAccounts { get; }
    DbSet<Investigation> Investigations { get; }
    DbSet<InvestigationReference> InvestigationReferences { get; }
    DbSet<InvestigationFinding> InvestigationFindings { get; }
    DbSet<MovementAuditDecision> MovementAuditDecisions { get; }
    DbSet<PlanningMonth> PlanningMonths { get; }
    DbSet<PlanningItem> PlanningItems { get; }
    DbSet<MovementIdentityLink> MovementIdentityLinks { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
