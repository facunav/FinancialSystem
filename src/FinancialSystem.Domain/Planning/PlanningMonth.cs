namespace FinancialSystem.Domain.Planning;

/// <summary>
/// Representa un único mes de planificación — la unidad básica del módulo de
/// Planificación Mensual (ver docs/Epics/Epica-PlanificacionMensual.md).
///
/// NO tiene relación con ninguna entidad de clasificación (Category, Counterparty,
/// FinancialAccount, ClassifiedMovement, etc.) — es deliberado, ver sección 5 de la
/// épica: garantiza que este módulo pueda existir, cambiar o eliminarse sin ningún
/// impacto sobre Movimientos.
///
/// ALCANCE DE ESTE PR:
///   Solo la entidad, su configuración EF y la migración. Ningún endpoint, página,
///   servicio ni lógica de negocio se agrega todavía.
/// </summary>
public class PlanningMonth
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Mes al que corresponde esta planificación.</summary>
    public DateTime Period { get; set; }

    /// <summary>
    /// Ingreso esperado del mes, opcional. Cargado manualmente por el usuario —
    /// nunca calculado ni sugerido a partir del historial (épica, sección 5).
    /// </summary>
    public decimal? ExpectedIncome { get; set; }

    /// <summary>Pagos esperados de este mes.</summary>
    public ICollection<PlanningItem> Items { get; set; } = [];
}
