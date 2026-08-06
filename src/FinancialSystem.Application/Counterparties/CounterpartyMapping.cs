using FinancialSystem.Domain.Entities;

namespace FinancialSystem.Application.Counterparties;

/// <summary>
/// Proyección de <see cref="Counterparty"/> a <see cref="CounterpartySummary"/> --
/// misma lógica que el antiguo <c>CounterpartyEndpoints.ToDto</c>, reutilizada ahora
/// por los Handlers de GetAll, GetById, Create y Update (PATCH-047). Requiere que
/// <see cref="Counterparty.DefaultCategory"/> ya esté cargada (Include) para poder
/// completar <see cref="CounterpartySummary.DefaultCategoryName"/>.
/// </summary>
public static class CounterpartyMapping
{
    public static CounterpartySummary ToSummary(Counterparty c) => new(
        c.Id,
        c.Name,
        c.Type.ToString(),
        c.DefaultCategoryId,
        c.DefaultCategory?.DisplayName,
        c.DefaultMovementType?.ToString(),
        c.DefaultFinancialImpact?.ToString(),
        c.IsDeactivated);
}
