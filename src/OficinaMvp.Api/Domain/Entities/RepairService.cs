using OficinaMvp.Api.Domain.Exceptions;

namespace OficinaMvp.Api.Domain.Entities;

public sealed class RepairService
{
    private RepairService()
    {
    }

    public RepairService(string name, string description, decimal laborPrice, int averageDurationMinutes)
    {
        Id = Guid.NewGuid();
        Update(name, description, laborPrice, averageDurationMinutes);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public decimal LaborPrice { get; private set; }
    public int AverageDurationMinutes { get; private set; }

    public void Update(string name, string description, decimal laborPrice, int averageDurationMinutes)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("O nome do serviço é obrigatório.");
        }

        if (laborPrice < 0)
        {
            throw new DomainException("O preço do serviço não pode ser negativo.");
        }

        if (averageDurationMinutes <= 0)
        {
            throw new DomainException("A duração média deve ser maior que zero.");
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? "Sem descrição" : description.Trim();
        LaborPrice = decimal.Round(laborPrice, 2, MidpointRounding.AwayFromZero);
        AverageDurationMinutes = averageDurationMinutes;
    }
}
