using FinancialSystem.Api.Endpoints;
using FluentValidation;

namespace FinancialSystem.Api.Validation;

/// <summary>
/// Patch 0065 (PATCH-016) -- ver CreateCategoryRequestValidator para el contexto
/// general del módulo piloto.
///
/// A diferencia de Create, CategoryEndpoints.Update trata un DisplayName nulo/vacío
/// como "no cambiar este campo" (no es un error) -- ese comportamiento se preserva acá
/// con When(...): la regla de longitud máxima solo se evalúa cuando efectivamente se
/// provee un valor no vacío, exactamente como hacía el `if` manual que reemplaza.
/// SortOrder no tenía ninguna validación manual y no se le agrega ninguna acá -- este
/// patch migra validación existente, no agrega reglas de negocio nuevas.
/// </summary>
public sealed class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public const int DisplayNameMaxLength = CreateCategoryRequestValidator.DisplayNameMaxLength;

    public UpdateCategoryRequestValidator()
    {
        RuleFor(r => r.DisplayName)
            .MaximumLength(DisplayNameMaxLength)
                .WithMessage($"displayName no puede superar los {DisplayNameMaxLength} caracteres")
            .When(r => !string.IsNullOrWhiteSpace(r.DisplayName));
    }
}
