namespace FinancialSystem.Api.DTOs;

// ── POST /api/investigations ──────────────────────────────────────────────────

public sealed record CreateInvestigationRequest(string Question, string? Tags);

public sealed record CreateInvestigationResponseDto(Guid Id, string Status, DateTime CreatedAt);
