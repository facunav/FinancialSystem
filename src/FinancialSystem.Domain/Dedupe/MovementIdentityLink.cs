using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Domain.Dedupe;

/// <summary>
/// Vínculo de identidad entre una representación física (hoy siempre un
/// <c>BankStatement</c>, vía <see cref="SourceEntityType"/>+<see cref="SourceId"/>) y el
/// grupo lógico que agrupa a todas las representaciones del mismo movimiento real
/// (<see cref="IdentityGroupId"/>).
///
/// REFERENCIA BLANDA, SIN FK -- mismo patrón ya usado por
/// <c>ClassifiedMovementItem</c>/<c>InvestigationReference</c> (ver sus doc-comments):
/// evita cascadas indeseadas sobre BankStatements y permite agregar fuentes nuevas sin
/// migrar este schema. Esta entidad NUNCA modifica ni borra la fila física que referencia.
///
/// CARDINALIDAD (verificada antes de implementar -- ver PRE-FLIGHT Etapa 4-CONV punto A):
/// una fila física pertenece, como máximo, a UN MovementIdentityLink -- garantizado por el
/// índice único (SourceEntityType, SourceId) en la configuración EF. Esto es seguro
/// ÚNICAMENTE porque solo se persisten relaciones FUERTE, y FUERTE exige, por definición
/// de la matriz (DEDUPE-003-CONV sección E), CANDIDATO_UNICO -- cero competidores en toda
/// la cuenta. POSIBLE/INDETERMINADO nunca llegan a esta tabla (ver
/// <see cref="IdentityClassification"/>), así que el caso que rompería 1→1 (un pendiente
/// con 2+ candidatos igualmente plausibles) nunca se intenta persistir acá.
///
/// CARRY-FORWARD: un grupo puede tener 3+ miembros (pendiente + liquidado + copias de
/// exportaciones sucesivas con firma idéntica) -- cada miembro tiene su PROPIA fila en
/// esta tabla, todas compartiendo el mismo IdentityGroupId. La cardinalidad 1→1 es por
/// fila física, no por grupo.
/// </summary>
public class MovementIdentityLink
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Agrupa todas las representaciones físicas del mismo movimiento real.</summary>
    public Guid IdentityGroupId { get; set; }

    /// <summary>Qué tabla contiene la representación física referenciada.</summary>
    public SourceEntityType SourceEntityType { get; set; }

    /// <summary>Id de la representación física en su tabla origen.</summary>
    public Guid SourceId { get; set; }

    public IdentityRole Role { get; set; }

    /// <summary>
    /// Siempre <see cref="IdentityClassification.Fuerte"/> para filas efectivamente
    /// persistidas -- ver doc-comment de la entidad y de <see cref="IdentityClassification"/>.
    /// Se conserva como columna (no una constante) para que el historial quede explícito
    /// y auditable, no implícito por "si existe la fila, es Fuerte".
    /// </summary>
    public IdentityClassification Classification { get; set; }

    /// <summary>
    /// Qué señales concretas (DEDUPE-003-CONV, tabla de señales A-M) sostienen esta
    /// clasificación -- texto libre, para auditoría humana. Nunca vacío para una fila Fuerte.
    /// </summary>
    public string Evidence { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>"DedupeEngine" (import en vivo) | "Backfill-DEDUPE-004-CONV" (backfill) | manual.</summary>
    public string CreatedBy { get; set; } = string.Empty;
}
