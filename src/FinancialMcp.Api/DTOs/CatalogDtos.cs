using FinancialSystem.Application.Accounts;

namespace FinancialSystem.Api.DTOs;

// ─────────────────────────────────────────────────────────────────────────────
// GET /api/categories
// ─────────────────────────────────────────────────────────────────────────────

public sealed record CategoryDto(Guid Id, string Name, string DisplayName, int SortOrder, bool IsDeactivated);

// ── GET /api/counterparties ───────────────────────────────────────────────────

public sealed record CounterpartyDto(
    Guid Id,
    string Name,
    string Type,
    Guid? DefaultCategoryId,
    string? DefaultCategoryName,
    string? DefaultMovementType,
    string? DefaultFinancialImpact,
    bool IsDeactivated);

// ── GET /api/accounts ─────────────────────────────────────────────────────────

public sealed record FinancialAccountDto(
    Guid Id,
    string Name,
    string Type,
    string? AccountNumber,
    string Currency,
    string? Notes,
    bool IsDeactivated)
{
    public static FinancialAccountDto Create(FinancialAccountSummary s) => new(
        s.Id,
        s.Name,
        s.Type.ToString(),
        s.AccountNumber,
        s.Currency,
        s.Notes,
        s.IsDeactivated);
}

// ── PUT .../financial-account (Transaction y BankStatement) ────────────────────

/// <summary>
/// Null desasigna la cuenta. Compartido entre TransactionEndpoints y
/// BankStatementEndpoints — misma semántica en ambos casos.
/// </summary>
public sealed record AssignFinancialAccountRequest(Guid? FinancialAccountId);
