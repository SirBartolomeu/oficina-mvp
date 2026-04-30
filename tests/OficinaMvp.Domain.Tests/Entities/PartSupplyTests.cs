using OficinaMvp.Api.Domain.Entities;
using OficinaMvp.Api.Domain.Exceptions;

namespace OficinaMvp.Domain.Tests.Entities;

public sealed class PartSupplyTests
{
    [Fact]
    public void Constructor_ShouldRoundUnitPrice()
    {
        var part = new PartSupply("Filtro de oleo", 10.555m, 10);

        Assert.Equal(10.56m, part.UnitPrice);
        Assert.Equal(10, part.StockQuantity);
    }

    [Fact]
    public void Update_ShouldThrow_WhenNameIsInvalid()
    {
        var part = new PartSupply("Filtro de oleo", 10m, 10);

        Assert.Throws<DomainException>(() => part.Update(" ", 10m, 10));
    }

    [Fact]
    public void Update_ShouldThrow_WhenPriceIsNegative()
    {
        var part = new PartSupply("Filtro de oleo", 10m, 10);

        Assert.Throws<DomainException>(() => part.Update("Filtro", -1m, 10));
    }

    [Fact]
    public void Update_ShouldThrow_WhenStockIsNegative()
    {
        var part = new PartSupply("Filtro de oleo", 10m, 10);

        Assert.Throws<DomainException>(() => part.Update("Filtro", 10m, -1));
    }

    [Fact]
    public void RemoveFromStock_ShouldDecreaseStock_WhenQuantityIsValid()
    {
        var part = new PartSupply("Filtro de oleo", 10m, 10);

        part.RemoveFromStock(3);

        Assert.Equal(7, part.StockQuantity);
    }

    [Fact]
    public void RemoveFromStock_ShouldThrow_WhenQuantityIsInvalid()
    {
        var part = new PartSupply("Filtro de oleo", 10m, 10);

        Assert.Throws<DomainException>(() => part.RemoveFromStock(0));
    }

    [Fact]
    public void RemoveFromStock_ShouldThrow_WhenStockIsInsufficient()
    {
        var part = new PartSupply("Filtro de oleo", 10m, 1);

        Assert.Throws<DomainException>(() => part.RemoveFromStock(2));
    }
}
