using OficinaMvp.Api.Domain.Exceptions;
using OficinaMvp.Api.Domain.Validation;

namespace OficinaMvp.Domain.Tests.Validation;

public sealed class DocumentValidatorTests
{
    [Theory]
    [InlineData("529.982.247-25")]
    [InlineData("52998224725")]
    [InlineData("04.252.011/0001-10")]
    [InlineData("04252011000110")]
    public void IsValid_ShouldReturnTrue_ForValidDocuments(string document)
    {
        var result = DocumentValidator.IsValid(document);
        Assert.True(result);
    }

    [Theory]
    [InlineData("111.111.111-11")]
    [InlineData("12345678901")]
    [InlineData("11.111.111/1111-11")]
    [InlineData("123")]
    public void IsValid_ShouldReturnFalse_ForInvalidDocuments(string document)
    {
        var result = DocumentValidator.IsValid(document);
        Assert.False(result);
    }

    [Fact]
    public void Normalize_ShouldThrow_WhenDocumentIsInvalid()
    {
        Assert.Throws<DomainException>(() => DocumentValidator.Normalize("11111111111"));
    }
}
