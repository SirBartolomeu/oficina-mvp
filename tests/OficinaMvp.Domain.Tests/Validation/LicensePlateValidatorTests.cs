using OficinaMvp.Api.Domain.Exceptions;
using OficinaMvp.Api.Domain.Validation;

namespace OficinaMvp.Domain.Tests.Validation;

public sealed class LicensePlateValidatorTests
{
    [Theory]
    [InlineData("ABC1234")]
    [InlineData("ABC-1234")]
    [InlineData("BRA2E19")]
    [InlineData("bra2e19")]
    public void IsValid_ShouldReturnTrue_ForValidPlates(string plate)
    {
        var result = LicensePlateValidator.IsValid(plate);
        Assert.True(result);
    }

    [Theory]
    [InlineData("AB12345")]
    [InlineData("AAAA123")]
    [InlineData("12A3BCD")]
    [InlineData("")]
    public void IsValid_ShouldReturnFalse_ForInvalidPlates(string plate)
    {
        var result = LicensePlateValidator.IsValid(plate);
        Assert.False(result);
    }

    [Fact]
    public void Normalize_ShouldThrow_WhenPlateIsInvalid()
    {
        Assert.Throws<DomainException>(() => LicensePlateValidator.Normalize("XXX1"));
    }
}
