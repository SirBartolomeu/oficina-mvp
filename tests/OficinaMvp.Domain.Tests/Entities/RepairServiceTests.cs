using OficinaMvp.Api.Domain.Entities;
using OficinaMvp.Api.Domain.Exceptions;

namespace OficinaMvp.Domain.Tests.Entities;

public sealed class RepairServiceTests
{
    [Fact]
    public void Constructor_ShouldSetDefaultDescription_WhenDescriptionIsBlank()
    {
        var service = new RepairService("Troca de oleo", " ", 120m, 60);

        Assert.StartsWith("Sem", service.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Update_ShouldThrow_WhenNameIsInvalid()
    {
        var service = new RepairService("Troca de oleo", "Descricao", 120m, 60);

        Assert.Throws<DomainException>(() => service.Update(" ", "Descricao", 120m, 60));
    }

    [Fact]
    public void Update_ShouldThrow_WhenPriceIsNegative()
    {
        var service = new RepairService("Troca de oleo", "Descricao", 120m, 60);

        Assert.Throws<DomainException>(() => service.Update("Troca", "Descricao", -1m, 60));
    }

    [Fact]
    public void Update_ShouldThrow_WhenDurationIsInvalid()
    {
        var service = new RepairService("Troca de oleo", "Descricao", 120m, 60);

        Assert.Throws<DomainException>(() => service.Update("Troca", "Descricao", 10m, 0));
    }
}
