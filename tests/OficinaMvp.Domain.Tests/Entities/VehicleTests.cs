using OficinaMvp.Api.Domain.Entities;
using OficinaMvp.Api.Domain.Exceptions;

namespace OficinaMvp.Domain.Tests.Entities;

public sealed class VehicleTests
{
    [Fact]
    public void Constructor_ShouldNormalizeLicensePlate()
    {
        var vehicle = new Vehicle(Guid.NewGuid(), "abc-1234", "Volkswagen", "Gol", 2020);

        Assert.Equal("ABC1234", vehicle.LicensePlate);
    }

    [Fact]
    public void Update_ShouldAcceptMercosulPlate()
    {
        var vehicle = new Vehicle(Guid.NewGuid(), "ABC1234", "Volkswagen", "Gol", 2020);

        vehicle.Update("bra2e19", "Fiat", "Pulse", 2023);

        Assert.Equal("BRA2E19", vehicle.LicensePlate);
        Assert.Equal("Fiat", vehicle.Brand);
        Assert.Equal("Pulse", vehicle.Model);
        Assert.Equal(2023, vehicle.Year);
    }

    [Fact]
    public void Update_ShouldThrow_WhenBrandIsInvalid()
    {
        var vehicle = new Vehicle(Guid.NewGuid(), "ABC1234", "Volkswagen", "Gol", 2020);

        Assert.Throws<DomainException>(() => vehicle.Update("ABC1234", " ", "Gol", 2020));
    }

    [Fact]
    public void Update_ShouldThrow_WhenModelIsInvalid()
    {
        var vehicle = new Vehicle(Guid.NewGuid(), "ABC1234", "Volkswagen", "Gol", 2020);

        Assert.Throws<DomainException>(() => vehicle.Update("ABC1234", "Volkswagen", " ", 2020));
    }

    [Fact]
    public void Update_ShouldThrow_WhenYearIsOutOfRange()
    {
        var vehicle = new Vehicle(Guid.NewGuid(), "ABC1234", "Volkswagen", "Gol", 2020);

        Assert.Throws<DomainException>(() => vehicle.Update("ABC1234", "Volkswagen", "Gol", 1949));
    }

    [Fact]
    public void Update_ShouldThrow_WhenPlateIsInvalid()
    {
        var vehicle = new Vehicle(Guid.NewGuid(), "ABC1234", "Volkswagen", "Gol", 2020);

        Assert.Throws<DomainException>(() => vehicle.Update("XXX1", "Volkswagen", "Gol", 2020));
    }
}
