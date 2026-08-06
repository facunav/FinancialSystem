namespace FinancialSystem.Application.Accounts.Commands;

/// <summary>
/// Type viaja sin parsear (a diferencia de Create) -- el orden original de validación
/// (NotFound, luego conflicto de Name, recién después Type inválido) se preserva
/// haciendo el parseo dentro del Handler, en la misma posición relativa que tenía en
/// FinancialAccountEndpoints.Update. Moverlo al Endpoint cambiaría cuál error gana
/// cuando ambos problemas ocurren a la vez.
/// </summary>
public sealed record UpdateFinancialAccountCommand(
    Guid FinancialAccountId,
    string? Name,
    string? Type,
    string? AccountNumber,
    string? Currency,
    string? Notes);
