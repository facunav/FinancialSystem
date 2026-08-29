using System.Data.Common;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Dedupe;
using FinancialSystem.Domain.Dedupe;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Dedupe;

/// <summary>
/// Implementación de <see cref="IMovementIdentityLinkRollbackService"/> -- DEDUPE-010.
/// Deliberadamente separado de <see cref="DedupeEngine"/>: no reutiliza ni modifica
/// Evaluate/PreviewAsync/ApplyAsync, no depende de ninguna señal de clasificación.
/// </summary>
internal sealed class MovementIdentityLinkRollbackService : IMovementIdentityLinkRollbackService
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public MovementIdentityLinkRollbackService(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<RollbackResult> RollbackAsync(
        Guid identityGroupId,
        string rolledBackBy,
        string reason,
        CancellationToken cancellationToken = default)
    {
        // Guardia de dominio -- no confía en que el llamador (el endpoint HTTP, o
        // cualquier otro caller futuro) ya haya validado esto; la persistencia de un
        // Reason vacío nunca debe ser posible desde este servicio.
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("reason es obligatorio y no puede estar vacío.", nameof(reason));

        var links = await _db.MovementIdentityLinks
            .Where(l => l.IdentityGroupId == identityGroupId)
            .ToListAsync(cancellationToken);

        if (links.Count == 0)
        {
            // El grupo no existe HOY en MovementIdentityLinks -- puede ser porque nunca
            // existió, o porque ya fue revertido antes (el rollback borra la fila real).
            // La auditoría es la única forma de distinguir ambos casos.
            var alreadyRolledBack = await _db.MovementIdentityLinkRollbacks
                .AnyAsync(r => r.IdentityGroupId == identityGroupId, cancellationToken);

            return new RollbackResult(
                alreadyRolledBack ? RollbackOutcome.AlreadyRolledBack : RollbackOutcome.NotFound,
                identityGroupId,
                0);
        }

        var rollback = new MovementIdentityLinkRollback
        {
            Id = Guid.NewGuid(),
            IdentityGroupId = identityGroupId,
            RolledBackBy = rolledBackBy,
            RolledBackAtUtc = _clock.UtcNow,
            Reason = reason,
        };

        // Snapshot exacto de cada fila ANTES de borrarla -- ver doc-comment de
        // MovementIdentityLinkRollbackMember. Nunca se toca BankStatements acá.
        var members = links.Select(l => new MovementIdentityLinkRollbackMember
        {
            Id = Guid.NewGuid(),
            RollbackId = rollback.Id,
            SourceEntityType = l.SourceEntityType,
            SourceId = l.SourceId,
            Role = l.Role,
            Classification = l.Classification,
            Evidence = l.Evidence,
            OriginalCreatedAtUtc = l.CreatedAtUtc,
            OriginalCreatedBy = l.CreatedBy,
        }).ToList();

        _db.MovementIdentityLinkRollbacks.Add(rollback);
        _db.MovementIdentityLinkRollbackMembers.AddRange(members);
        _db.MovementIdentityLinks.RemoveRange(links);

        // Un único SaveChangesAsync -- la inserción de la auditoría (rollback + members)
        // y el borrado de los links viajan en la misma transacción implícita de EF Core:
        // todo o nada, nunca un grupo parcialmente eliminado ni una auditoría sin borrado
        // correspondiente.
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return new RollbackResult(RollbackOutcome.RolledBack, identityGroupId, members.Count);
        }
        catch (DbUpdateException ex) when (ex.InnerException is DbException dbEx && dbEx.SqlState == "23505")
        {
            // Otra corrida concurrente ya insertó el registro de rollback para este mismo
            // IdentityGroupId (índice único real, backstop de concurrencia -- ver
            // doc-comment de MovementIdentityLinkRollback) entre el momento en que
            // leímos "no está revertido todavía" y este SaveChangesAsync. La violación de
            // unicidad aborta TODO el SaveChangesAsync (Postgres revierte la transacción
            // completa ante un INSERT fallido) -- el DELETE de los links, si esta corrida
            // hubiera llegado a intentarlo, tampoco queda persistido: nunca hay un estado
            // parcial, y nunca se duplica la auditoría.
            return new RollbackResult(RollbackOutcome.AlreadyRolledBack, identityGroupId, 0);
        }
    }
}
