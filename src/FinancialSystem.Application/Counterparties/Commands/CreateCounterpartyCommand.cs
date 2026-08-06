using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Application.Counterparties.Commands;

/// <summary>
/// <see cref="Type"/> ya viene parseado (o null si no se proporcionó) -- el parseo del
/// string del request y el BadRequest ante un valor inválido son validación de forma
/// del Endpoint (PATCH-047); la decisión de qué hacer cuando no se informa (default a
/// <see cref="CounterpartyType.Other"/>) es la única parte de esa lógica que sigue
/// siendo una regla de negocio y por eso vive en el Handler.
/// </summary>
public sealed record CreateCounterpartyCommand(
    string Name,
    CounterpartyType? Type,
    string? Notes,
    Guid? DefaultCategoryId,
    string? DefaultMovementType,
    string? DefaultFinancialImpact);
