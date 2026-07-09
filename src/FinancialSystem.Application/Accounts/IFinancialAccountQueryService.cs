using FinancialSystem.Domain.Entities;

namespace FinancialSystem.Application.Accounts;

// ── Modelo de resultado ────────────────────────────────────────────────────────
// Neutro: no es FinancialAccount (entidad EF), ni DTO de HTTP. Existe para que el
// modelo interno pueda evolucionar sin romper el contrato público de la API — el
// mapeo entidad → este modelo vive en FinancialAccountQueryService (Infrastructure);
// el mapeo modelo → DTO de HTTP vive en FinancialSystem.Api.DTOs (ver PR J2).

public sealed record FinancialAccountSummary(
    Guid Id,
    string Name,
    FinancialAccountType Type,
    string? AccountNumber,
    string Currency,
    string? Notes,
    bool IsDeactivated,
    DateTime CreatedAt,
    DateTime UpdatedAt);

// ── Interfaz del servicio ─────────────────────────────────────────────────────

/// <summary>
/// Queries de solo lectura sobre el catálogo de FinancialAccount. Nunca persiste
/// nada — la creación/edición/desactivación se maneja directamente en los
/// endpoints (mismo criterio que Category/Counterparty). Consumida por los
/// endpoints de /api/accounts (ver PR J2).
/// </summary>
public interface IFinancialAccountQueryService
{
    /// <summary>Cuentas activas por defecto, ordenadas por nombre. Filtro opcional por texto en el nombre.</summary>
    Task<IReadOnlyList<FinancialAccountSummary>> GetAllAsync(
        bool includeDeactivated = false, string? search = null, CancellationToken ct = default);

    /// <summary>Detalle de una cuenta puntual. Null si no existe.</summary>
    Task<FinancialAccountSummary?> GetByIdAsync(Guid id, CancellationToken ct = default);
}
