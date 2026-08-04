using FinancialSystem.Api.Endpoints;
using FluentValidation;

namespace FinancialSystem.Api.Validation;

/// <summary>
/// Patch 0065 (PATCH-016): módulo piloto de validación estructurada con
/// FluentValidation -- ver Validation/README para el porqué de FluentValidation y por
/// qué el alcance queda limitado a Categories en este patch.
///
/// Migra la validación manual que tenía CategoryEndpoints.Create
/// (string.IsNullOrWhiteSpace(request.DisplayName)) a una regla equivalente, más el
/// límite de longitud que ya declaraba CategoryConfiguration (Infrastructure) pero que
/// hoy nadie valida antes de llegar a la base -- Name/DisplayName se mantienen acá
/// como constantes propias en vez de referenciar la configuración de EF Core
/// directamente (esta clase vive en la capa Api, no debe depender de Infrastructure).
/// </summary>
public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    // Mismos límites que builder.Property(...).HasMaxLength(...) en
    // CategoryConfiguration.cs (Infrastructure) -- si esos cambian, estos deben
    // actualizarse junto con ellos; se dejan como constantes acá porque esta capa no
    // referencia Infrastructure.
    public const int DisplayNameMaxLength = 128;
    public const int NameMaxLength = 64;

    public CreateCategoryRequestValidator()
    {
        RuleFor(r => r.DisplayName)
            .NotEmpty().WithMessage("displayName es requerido")
            .MaximumLength(DisplayNameMaxLength)
                .WithMessage($"displayName no puede superar los {DisplayNameMaxLength} caracteres");

        // Name es opcional (se deriva de DisplayName si no se provee, ver
        // CategoryEndpoints.NormalizeName) -- solo se valida su longitud cuando el
        // caller efectivamente manda un valor.
        RuleFor(r => r.Name)
            .MaximumLength(NameMaxLength)
                .WithMessage($"name no puede superar los {NameMaxLength} caracteres")
            .When(r => !string.IsNullOrWhiteSpace(r.Name));
    }
}
