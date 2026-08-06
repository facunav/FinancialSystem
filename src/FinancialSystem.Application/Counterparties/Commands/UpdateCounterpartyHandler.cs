using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Counterparties.Commands;

/// <summary>
/// Migrado desde CounterpartyEndpoints.Update (PATCH-047) -- actualización parcial
/// idéntica: cada campo solo se toca si viene informado, Notes vacío/blanco limpia el
/// campo (null) mientras que Notes null significa "no tocar" (misma distinción que el
/// código original), DefaultCategoryId solo se puede asignar (no limpiar) desde acá, y
/// Default* de tipo enum se parsean "best effort" -- un valor no parseable se ignora en
/// silencio, sin error. Tras guardar, se recarga con Include(DefaultCategory) para
/// poder completar DefaultCategoryName en la respuesta -- mismo criterio que el
/// endpoint original.
/// </summary>
public sealed class UpdateCounterpartyHandler
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateCounterpartyHandler(IApplicationDbContext db, IDateTimeProvider dateTimeProvider)
    {
        _db = db;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<UpdateCounterpartyResult> Handle(
        UpdateCounterpartyCommand command, CancellationToken cancellationToken = default)
    {
        var c = await _db.Counterparties.FindAsync([command.CounterpartyId], cancellationToken);
        if (c is null) return UpdateCounterpartyResult.NotFound();

        if (!string.IsNullOrWhiteSpace(command.Name))
            c.Name = command.Name.Trim();
        if (command.Notes is not null)
            c.Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim();
        if (command.DefaultCategoryId is not null)
            c.DefaultCategoryId = command.DefaultCategoryId;
        if (!string.IsNullOrWhiteSpace(command.DefaultMovementType) &&
            Enum.TryParse<MovementType>(command.DefaultMovementType, ignoreCase: true, out var mt))
            c.DefaultMovementType = mt;
        if (!string.IsNullOrWhiteSpace(command.DefaultFinancialImpact) &&
            Enum.TryParse<FinancialImpact>(command.DefaultFinancialImpact, ignoreCase: true, out var fi))
            c.DefaultFinancialImpact = fi;

        c.UpdatedAt = _dateTimeProvider.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var updated = await _db.Counterparties
            .AsNoTracking()
            .Include(x => x.DefaultCategory)
            .FirstAsync(x => x.Id == command.CounterpartyId, cancellationToken);

        return UpdateCounterpartyResult.Success(CounterpartyMapping.ToSummary(updated));
    }
}
