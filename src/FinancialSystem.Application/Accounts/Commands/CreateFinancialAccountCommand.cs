using FinancialSystem.Domain.Entities;

namespace FinancialSystem.Application.Accounts.Commands;

/// <summary>
/// Type ya viene parseado -- el parseo del string del request y el BadRequest ante un
/// valor inválido son validación de forma del Endpoint (PATCH-048), antes de armar
/// este Command. A diferencia de Counterparty, Type es obligatorio acá (no hay
/// default): si no se informa o es inválido, el Endpoint responde 400 sin llegar al
/// Handler.
/// </summary>
public sealed record CreateFinancialAccountCommand(
    string Name,
    FinancialAccountType Type,
    string? AccountNumber,
    string? Currency,
    string? Notes);
