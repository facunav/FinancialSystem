using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Memory;

namespace FinancialSystem.Application.Investigations.Commands;

/// <summary>
/// Crea una Investigation (Status=Open, Conclusion=null, sin References) y la persiste
/// vía IApplicationDbContext — mismo patrón que ClassifyMovementHandler. Sin lógica
/// adicional: nada de memoria automática, Ollama, ni referencias iniciales.
/// </summary>
public sealed class CreateInvestigationHandler
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateInvestigationHandler(IApplicationDbContext db, IDateTimeProvider dateTimeProvider)
    {
        _db = db;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Investigation> Handle(
        CreateInvestigationCommand command, CancellationToken cancellationToken = default)
    {
        var now = _dateTimeProvider.UtcNow;

        var investigation = new Investigation
        {
            Question = command.Question,
            Tags = command.Tags,
            Status = InvestigationStatus.Open,
            Conclusion = null,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.Investigations.Add(investigation);
        await _db.SaveChangesAsync(cancellationToken);

        return investigation;
    }
}
