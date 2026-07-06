namespace FinancialMcp.Api.DTOs
{
    // ─────────────────────────────────────────────────────────────────────────────
    // GET /api/categories
    // ─────────────────────────────────────────────────────────────────────────────

    public sealed record CategoryDto(Guid Id, string Name, string DisplayName, int SortOrder, bool IsDeactivated);

    // ── GET /api/counterparties ───────────────────────────────────────────────────

    public sealed record CounterPartyDto(
        Guid Id,
        string Name,
        string Type,
        Guid? DefaultCategoryId,
        string? DefaultCategoryName,
        string? DefaultMovementType,
        string? DefaultFinancialImpact,
        bool IsDeactivated);
}
