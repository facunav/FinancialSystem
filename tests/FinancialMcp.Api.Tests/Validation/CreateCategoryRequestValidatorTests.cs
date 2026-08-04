using FinancialSystem.Api.Endpoints;
using FinancialSystem.Api.Validation;
using Xunit;

namespace FinancialMcp.Api.Tests.Validation;

/// <summary>
/// Cubre CreateCategoryRequestValidator (Patch 0065, PATCH-016) de forma aislada, sin
/// HTTP -- ver CategoryEndpointsValidationTests para la cobertura end-to-end del
/// endpoint (que reutiliza este mismo validador vía inyección de dependencias).
/// </summary>
public class CreateCategoryRequestValidatorTests
{
    private readonly CreateCategoryRequestValidator _validator = new();

    [Fact]
    public void Validate_WithValidRequest_Succeeds()
    {
        var result = _validator.Validate(new CreateCategoryRequest("Salud", null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithValidRequest_AndExplicitName_Succeeds()
    {
        var result = _validator.Validate(new CreateCategoryRequest("Salud", "Health"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithNullDisplayName_Fails()
    {
        var result = _validator.Validate(new CreateCategoryRequest(null!, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCategoryRequest.DisplayName));
    }

    [Fact]
    public void Validate_WithEmptyDisplayName_Fails()
    {
        var result = _validator.Validate(new CreateCategoryRequest(string.Empty, null));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_WithWhitespaceOnlyDisplayName_Fails()
    {
        var result = _validator.Validate(new CreateCategoryRequest("   ", null));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_WithDisplayNameExceedingMaxLength_Fails()
    {
        var tooLong = new string('a', CreateCategoryRequestValidator.DisplayNameMaxLength + 1);

        var result = _validator.Validate(new CreateCategoryRequest(tooLong, null));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_WithDisplayNameAtMaxLength_Succeeds()
    {
        var atLimit = new string('a', CreateCategoryRequestValidator.DisplayNameMaxLength);

        var result = _validator.Validate(new CreateCategoryRequest(atLimit, null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithNameExceedingMaxLength_Fails()
    {
        var tooLong = new string('a', CreateCategoryRequestValidator.NameMaxLength + 1);

        var result = _validator.Validate(new CreateCategoryRequest("Salud", tooLong));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCategoryRequest.Name));
    }

    [Fact]
    public void Validate_WithNameAtMaxLength_Succeeds()
    {
        var atLimit = new string('a', CreateCategoryRequestValidator.NameMaxLength);

        var result = _validator.Validate(new CreateCategoryRequest("Salud", atLimit));

        Assert.True(result.IsValid);
    }
}
