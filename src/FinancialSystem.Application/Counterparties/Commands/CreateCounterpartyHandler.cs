using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Application.Counterparties.Commands;

/// <summary>
/// Migrado desde CounterpartyEndpoints.Create (PATCH-047) -- misma lógica exacta: si no
/// se informa Type, se usa <see cref="CounterpartyType.Other"/> (ver el análisis de la
/// entidad Counterparty: no tiene ningún consumidor hoy fuera de esta validación).
/// DefaultMovementType/DefaultFinancialImpact se parsean "best effort" -- un valor no
/// parseable se ignora en silencio (null), sin error, igual que el código original. No
/// hay validación de unicidad de Name para Counterparty (a diferencia de Category) --
/// no se agrega ninguna acá. Sin Result envolvente: esta operación no tiene ningún
/// camino de fallo, igual que CreateInvestigationHandler.
/// </summary>
public sealed class CreateCounterpartyHandler
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateCounterpartyHandler(IApplicationDbContext db, IDateTimeProvider dateTimeProvider)
    {
        _db = db;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Counterparty> Handle(
        CreateCounterpartyCommand command, CancellationToken cancellationToken = default)
    {
        MovementType? defaultMovementType = null;
        if (!string.IsNullOrWhiteSpace(command.DefaultMovementType) &&
            Enum.TryParse<MovementType>(command.DefaultMovementType, ignoreCase: true, out var mt))
            defaultMovementType = mt;

        FinancialImpact? defaultImpact = null;
        if (!string.IsNullOrWhiteSpace(command.DefaultFinancialImpact) &&
            Enum.TryParse<FinancialImpact>(command.DefaultFinancialImpact, ignoreCase: true, out var fi))
            defaultImpact = fi;

        var now = _dateTimeProvider.UtcNow;

        var counterparty = new Counterparty
        {
            Name = command.Name.Trim(),
            Type = command.Type ?? CounterpartyType.Other,
            Notes = command.Notes?.Trim(),
            DefaultCategoryId = command.DefaultCategoryId,
            DefaultMovementType = defaultMovementType,
            DefaultFinancialImpact = defaultImpact,
            IsDeactivated = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.Counterparties.Add(counterparty);
        await _db.SaveChangesAsync(cancellationToken);

        return counterparty;
    }
}
