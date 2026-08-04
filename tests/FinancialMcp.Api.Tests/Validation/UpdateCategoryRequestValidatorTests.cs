using FinancialSystem.Api.Endpoints;
using FinancialSystem.Api.Validation;
using Xunit;

namespace FinancialMcp.Api.Tests.Validation;

/// <summary>
/// Cubre UpdateCategoryRequestValidator (Patch 0065, PATCH-016) de forma aislada, sin
/// HTTP -- ver CategoryEndpointsValidationTests para la cobertura end-to-end.
/// </summary>
public class UpdateCategoryRequestValidatorTests
{
    private readonly UpdateCategoryRequestValidator _validator = new();

    [Fact]
    public void Validate_WithValidDisplayName_Succeeds()
    {
        var result = _validator.Validate(new UpdateCategoryRequest("Salud", null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithNullDisplayName_Succeeds()
    {
        // A diferencia de Create, un DisplayName nulo en Update no es un error --
        // significa "no cambiar este campo" (ver CategoryEndpoints.Update).
        var result = _validator.Validate(new UpdateCategoryRequest(null, null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithWhitespaceOnlyDisplayName_Succeeds()
    {
        var result = _validator.Validate(new UpdateCategoryRequest("   ", null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithDisplayNameExceedingMaxLength_Fails()
    {
        var tooLong = new string('a', UpdateCategoryRequestValidator.DisplayNameMaxLength + 1);

        var result = _validator.Validate(new UpdateCategoryRequest(tooLong, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCategoryRequest.DisplayName));
    }

    [Fact]
    public void Validate_WithDisplayNameAtMaxLength_Succeeds()
    {
        var atLimit = new string('a', UpdateCategoryRequestValidator.DisplayNameMaxLength);

        var result = _validator.Validate(new UpdateCategoryRequest(atLimit, null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithSortOrderOnly_Succeeds()
    {
        // SortOrder no tiene ninguna regla (sección "no agregar reglas de negocio
        // nuevas" del patch) -- cualquier valor pasa.
        var result = _validator.Validate(new UpdateCategoryRequest(null, -100));

        Assert.True(result.IsValid);
    }
}
