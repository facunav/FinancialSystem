using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Domain.Entities;

/// <summary>
/// Contraparte de un movimiento financiero. Responde "¿con quién o qué se relaciona?".
///
/// Es una de las 4 dimensiones de clasificación de ClassifiedMovement.
/// Reemplaza el concepto de "beneficiario" o cualquier referencia a "destinatario"
/// del modelo anterior.
///
/// VALORES SUGERIDOS:
///   Cada contraparte puede configurar valores por defecto para CategoryId,
///   MovementType y FinancialImpact. Durante la revisión de un movimiento,
///   si el usuario selecciona una contraparte conocida, la UI pre-carga
///   esos valores — reduciendo la fricción en movimientos recurrentes.
///   Ejemplo: "Farmacia Amancay" → Category=Salud, Type=Purchase, Impact=Expense.
///
/// ADMINISTRABLE POR CRUD:
///   Las contrapartes pueden crearse, editarse y desactivarse.
///   No se eliminan físicamente para preservar la referencia en históricos.
///
/// APRENDIZAJE FUTURO:
///   A futuro, el sistema podrá sugerir la contraparte automáticamente
///   basándose en la similitud de descripción del movimiento con
///   descripciones históricas ya clasificadas.
/// </summary>
public class Counterparty
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Nombre visible en la UI. Ejemplos: "Farmacia Amancay", "Netflix", "BBVA".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Tipo de contraparte según su naturaleza.</summary>
    public CounterpartyType Type { get; set; }

    /// <summary>
    /// Notas libres del usuario sobre esta contraparte.
    /// Ejemplos: "sucursal del shopping", "cuenta de Tati".
    /// </summary>
    public string? Notes { get; set; }

    // ── Valores sugeridos por defecto ────────────────────────────────────────
    // Cuando el usuario selecciona esta contraparte al clasificar un movimiento,
    // la UI propone estos valores. El usuario puede aceptarlos o cambiarlos.

    /// <summary>Categoría que se sugiere automáticamente al elegir esta contraparte. Nullable.</summary>
    public Guid? DefaultCategoryId { get; set; }
    public Category? DefaultCategory { get; set; }

    /// <summary>Tipo de movimiento que se sugiere al elegir esta contraparte. Nullable.</summary>
    public MovementType? DefaultMovementType { get; set; }

    /// <summary>Impacto financiero que se sugiere al elegir esta contraparte. Nullable.</summary>
    public FinancialImpact? DefaultFinancialImpact { get; set; }

    // ── Estado y auditoría ────────────────────────────────────────────────────

    /// <summary>
    /// False = activa (aparece en selectores).
    /// True = desactivada (no aparece en nuevos movimientos pero históricos la siguen referenciando).
    /// </summary>
    public bool IsDeactivated { get; set; }

    /// <summary>Cuándo se creó la contraparte.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Cuándo se modificó por última vez.</summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Tipo de contraparte según su naturaleza.
/// Responde "¿qué tipo de entidad es la contraparte?".
/// </summary>
public enum CounterpartyType
{
    /// <summary>Persona física (familiar, amigo, conocido).</summary>
    Person = 1,

    /// <summary>Comercio (supermercado, farmacia, restaurante, etc.).</summary>
    Business = 2,

    /// <summary>Empresa (empleador, proveedor corporativo).</summary>
    Company = 3,

    /// <summary>Entidad bancaria o financiera.</summary>
    Bank = 4,

    /// <summary>Servicio (electricidad, gas, internet, streaming).</summary>
    Service = 5,

    /// <summary>Organismo gubernamental (AFIP, municipio, etc.).</summary>
    Government = 6,

    /// <summary>Cuenta propia del mismo usuario en otro banco/entidad.</summary>
    OwnAccount = 7,

    /// <summary>Tarjeta de crédito propia (para registrar pagos de resumen).</summary>
    OwnCard = 8,

    /// <summary>Vehículo de inversión (plazo fijo, FCI, broker, etc.).</summary>
    Investment = 9,

    /// <summary>No encaja en ninguna categoría anterior.</summary>
    Other = 99,
}