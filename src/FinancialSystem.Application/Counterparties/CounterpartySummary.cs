namespace FinancialSystem.Application.Counterparties;

/// <summary>
/// Forma de salida compartida por las operaciones de Counterparty (GetAll, GetById,
/// Create, Update) -- evita 4 proyecciones casi idénticas repetidas en cada Handler.
/// Distinta de <c>FinancialSystem.Api.DTOs.CounterpartyDto</c> (Application no depende
/// de Api) aunque hoy tenga los mismos campos.
/// </summary>
public sealed record CounterpartySummary(
    Guid Id,
    string Name,
    string Type,
    Guid? DefaultCategoryId,
    string? DefaultCategoryName,
    string? DefaultMovementType,
    string? DefaultFinancialImpact,
    bool IsDeactivated);
