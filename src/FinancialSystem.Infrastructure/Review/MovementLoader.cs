using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Review;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Review;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Review;

internal sealed class MovementLoader : IMovementLoader
{
    private readonly IApplicationDbContext _db;

    public MovementLoader(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<FinancialMovement>> LoadAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var bankStatements = await _db.BankStatements
            .AsNoTracking()
            .Where(b => b.Date >= fromUtc && b.Date <= toUtc)
            .ToListAsync(cancellationToken);

        return bankStatements.ConvertAll(ToFinancialMovement);
    }

    private static FinancialMovement ToFinancialMovement(BankStatement statement) => new()
    {
        Date = statement.Date,
        Description = statement.Concept,
        // BankStatement: positivo = crédito/ingreso, negativo = débito/egreso.
        // FinancialMovement: positivo = gasto/débito, negativo = ingreso/crédito.
        // Signo invertido a propósito al adaptar entre los dos modelos.
        Amount = -statement.Amount,
        Currency = statement.Currency,
        Source = MovementSource.BankDebit,
        OriginalId = statement.Id.ToString(),
        SourceFile = statement.SourceFile,
    };
}
