namespace FinancialSystem.Domain.Enums;

/// <summary>
/// Estado de una Investigation dentro de su ciclo de vida.
/// Modelo conceptual fijado en ADR-007 (Memoria del Financial MCP), §5.
/// </summary>
public enum InvestigationStatus
{
    /// <summary>Se creó la investigación, todavía sin desarrollo.</summary>
    Open = 1,

    /// <summary>Tiene desarrollo pero todavía no llegó a una conclusión.</summary>
    InProgress = 2,

    /// <summary>Llegó a una conclusión.</summary>
    Resolved = 3,

    /// <summary>Se determinó que no amerita seguir investigándose, sin conclusión.</summary>
    Discarded = 4,
}
