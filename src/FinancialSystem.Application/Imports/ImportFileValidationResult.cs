namespace FinancialSystem.Application.Imports;

/// <summary>Resultado de <see cref="IImportFileValidator.Validate"/>.</summary>
public sealed record ImportFileValidationResult(bool IsValid, string? RejectionReason)
{
    public static readonly ImportFileValidationResult Valid = new(true, null);

    public static ImportFileValidationResult Invalid(string reason) =>
        new(false, reason);
}
