using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Dedupe;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Memory;
using FinancialSystem.Domain.Planning;
using FinancialSystem.Domain.Review;
using FinancialSystem.Infrastructure.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<BankStatement> BankStatements => Set<BankStatement>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ClassifiedMovement> ClassifiedMovements => Set<ClassifiedMovement>();
    public DbSet<ClassifiedMovementItem> ClassifiedMovementItems => Set<ClassifiedMovementItem>();
    public DbSet<Counterparty> Counterparties => Set<Counterparty>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportBatchLine> ImportBatchLines => Set<ImportBatchLine>();
    public DbSet<FinancialAccount> FinancialAccounts => Set<FinancialAccount>();
    public DbSet<Investigation> Investigations => Set<Investigation>();
    public DbSet<InvestigationReference> InvestigationReferences => Set<InvestigationReference>();
    public DbSet<InvestigationFinding> InvestigationFindings => Set<InvestigationFinding>();
    public DbSet<MovementAuditDecision> MovementAuditDecisions => Set<MovementAuditDecision>();
    public DbSet<PlanningMonth> PlanningMonths => Set<PlanningMonth>();
    public DbSet<PlanningItem> PlanningItems => Set<PlanningItem>();
    public DbSet<MovementIdentityLink> MovementIdentityLinks => Set<MovementIdentityLink>();
    public DbSet<MovementIdentityLinkRollback> MovementIdentityLinkRollbacks => Set<MovementIdentityLinkRollback>();
    public DbSet<MovementIdentityLinkRollbackMember> MovementIdentityLinkRollbackMembers => Set<MovementIdentityLinkRollbackMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new TransactionConfiguration());
        modelBuilder.ApplyConfiguration(new BankStatementConfiguration());
        modelBuilder.ApplyConfiguration(new CategoryConfiguration());
        modelBuilder.ApplyConfiguration(new ClassifiedMovementConfiguration());
        modelBuilder.ApplyConfiguration(new ClassifiedMovementItemConfiguration());
        modelBuilder.ApplyConfiguration(new CounterpartyConfiguration());
        modelBuilder.ApplyConfiguration(new ImportBatchConfiguration());
        modelBuilder.ApplyConfiguration(new ImportBatchLineConfiguration());
        modelBuilder.ApplyConfiguration(new FinancialAccountConfiguration());
        modelBuilder.ApplyConfiguration(new InvestigationConfiguration());
        modelBuilder.ApplyConfiguration(new InvestigationReferenceConfiguration());
        modelBuilder.ApplyConfiguration(new InvestigationFindingConfiguration());
        modelBuilder.ApplyConfiguration(new MovementAuditDecisionConfiguration());
        modelBuilder.ApplyConfiguration(new PlanningMonthConfiguration());
        modelBuilder.ApplyConfiguration(new PlanningItemConfiguration());
        modelBuilder.ApplyConfiguration(new MovementIdentityLinkConfiguration());
        modelBuilder.ApplyConfiguration(new MovementIdentityLinkRollbackConfiguration());
        modelBuilder.ApplyConfiguration(new MovementIdentityLinkRollbackMemberConfiguration());
    }
}
