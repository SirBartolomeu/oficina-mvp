using OficinaMvp.Api.Domain.Exceptions;
using OficinaMvp.Api.Domain.Validation;

namespace OficinaMvp.Api.Domain.Entities;

public sealed class Vehicle
{
    private Vehicle()
    {
    }

    public Vehicle(Guid customerId, string licensePlate, string brand, string model, int year)
    {
        Id = Guid.NewGuid();
        CustomerId = customerId;
        Update(licensePlate, brand, model, year);
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public string LicensePlate { get; private set; } = string.Empty;
    public string Brand { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public int Year { get; private set; }

    public Customer? Customer { get; private set; }

    public void Update(string licensePlate, string brand, string model, int year)
    {
        if (string.IsNullOrWhiteSpace(brand))
        {
            throw new DomainException("A marca do veículo é obrigatória.");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new DomainException("O modelo do veículo é obrigatório.");
        }

        var currentYear = DateTime.UtcNow.Year + 1;
        if (year is < 1950 || year > currentYear)
        {
            throw new DomainException($"Ano inválido. Informe um valor entre 1950 e {currentYear}.");
        }

        LicensePlate = LicensePlateValidator.Normalize(licensePlate);
        Brand = brand.Trim();
        Model = model.Trim();
        Year = year;
    }
}
