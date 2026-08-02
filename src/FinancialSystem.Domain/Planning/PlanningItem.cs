namespace FinancialSystem.Domain.Planning;

/// <summary>
/// Representa una obligación de pago dentro de un mes de planificación. No
/// representa un movimiento bancario, una categoría, una factura ni una
/// contraparte — únicamente algo que el usuario quiere recordar pagar ese mes
/// (ver docs/Epics/Epica-PlanificacionMensual.md, sección 5).
/// </summary>
public class PlanningItem
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid PlanningMonthId { get; set; }
    public PlanningMonth? PlanningMonth { get; set; }

    /// <summary>Texto libre, ej. "Internet", "Visa".</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Cargado manualmente por el usuario; puede quedar vacío hasta que se conoce
    /// el valor real de la factura. El sistema nunca lo estima (épica, sección 6.5).
    /// </summary>
    public decimal? ExpectedAmount { get; set; }

    /// <summary>Dato puramente descriptivo — nunca dispara alertas ni recordatorios (épica, sección 7).</summary>
    public DateTime? DueDate { get; set; }

    public bool IsPaid { get; set; }

    public DateTime? PaidAt { get; set; }
}
