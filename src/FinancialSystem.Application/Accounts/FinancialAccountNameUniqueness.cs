using FinancialSystem.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Accounts;

/// <summary>
/// Chequeo de unicidad de Name entre cuentas activas -- compartido por Create, Update y
/// Reactivate (PATCH-048), los 3 puntos donde el módulo original invocaba
/// NameIsTakenByActiveAccount (antes duplicado en FinancialAccountEndpoints, usado
/// idénticamente por sus 3 métodos). Comparación case-insensitive, igual que el código
/// original. excludeId=null (Create) no excluye ninguna cuenta.
/// </summary>
public static class FinancialAccountNameUniqueness
{
    public static Task<bool> IsTakenByActiveAccountAsync(
        IApplicationDbContext db, string name, Guid? excludeId, CancellationToken ct)
    {
        var normalized = name.ToLower();
        return db.FinancialAccounts.AnyAsync(
            a => !a.IsDeactivated && a.Id != excludeId && a.Name.ToLower() == normalized, ct);
    }
}
