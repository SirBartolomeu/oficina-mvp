using OficinaMvp.Api.Domain.Entities;
using OficinaMvp.Api.Domain.Exceptions;

namespace OficinaMvp.Domain.Tests.Entities;

public sealed class CustomerTests
{
    [Fact]
    public void Constructor_ShouldNormalizeDocumentAndTrimOptionalFields()
    {
        var customer = new Customer("Joao", "529.982.247-25", " 11999999999 ", " joao@email.com ");

        Assert.Equal("52998224725", customer.Document);
        Assert.Equal("11999999999", customer.Phone);
        Assert.Equal("joao@email.com", customer.Email);
    }

    [Fact]
    public void Update_ShouldSetOptionalFieldsToNull_WhenValuesAreBlank()
    {
        var customer = new Customer("Joao", "52998224725", "11999999999", "joao@email.com");

        customer.Update("Joao da Silva", "04252011000110", " ", " ");

        Assert.Equal("04252011000110", customer.Document);
        Assert.Null(customer.Phone);
        Assert.Null(customer.Email);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenNameIsInvalid()
    {
        Assert.Throws<DomainException>(() => new Customer(" ", "52998224725", null, null));
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenDocumentIsInvalid()
    {
        Assert.Throws<DomainException>(() => new Customer("Joao", "11111111111", null, null));
    }
}
