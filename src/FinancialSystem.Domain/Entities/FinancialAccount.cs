namespace FinancialSystem.Domain.Entities;

/// <summary>
/// Cuenta financiera explícita (banco, tarjeta, efectivo, inversión). Responde
/// "¿de qué cuenta salió o entró este movimiento?" — un eje distinto y ortogonal
/// a Counterparty ("¿con quién se relaciona el movimiento?").
///
/// ADMINISTRABLE POR CRUD, igual que Category/Counterparty. No se elimina
/// físicamente — se desactiva, para preservar la referencia en históricos.
///
/// RELACIÓN CON BankStatement/Transaction:
///   FK opcional (nullable) desde ambas — un movimiento puede existir sin cuenta
///   asignada. La asignación es manual por ahora; no hay wiring automático desde
///   el pipeline de importación (ver docs/RoadMaps/FinancialMcp-vNext.md, Épica J).
///
/// BASE PARA ÉPICA M:
///   Type=Investment es un valor válido desde ahora (ver ADR-004) pero no habilita
///   ningún comportamiento de inversión todavía — InvestmentAccount (Épica M) es
///   una entidad separada que se apoya en esta, con saldo/valuación propios.
/// </summary>
public class FinancialAccount
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Nombre visible en la UI. Ejemplos: "BBVA Caja de Ahorro", "Visa BBVA".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Tipo de cuenta.</summary>
    public FinancialAccountType Type { get; set; }

    /// <summary>Número de cuenta o tarjeta, si aplica. Solo referencia — no se usa para matching automático.</summary>
    public string? AccountNumber { get; set; }

    public string Currency { get; set; } = "ARS";

    /// <summary>Notas libres del usuario sobre esta cuenta.</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// False = activa (aparece en selectores).
    /// True = desactivada (no aparece en nuevas asignaciones; los históricos la siguen referenciando).
    /// </summary>
    public bool IsDeactivated { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Tipo de cuenta financiera. Ver docs/Decisions/ADR-004-financial-account-antes-de-inversiones.md.
/// </summary>
public enum FinancialAccountType
{
    Bank = 1,
    Card = 2,
    Investment = 3,
    Cash = 4,
}
