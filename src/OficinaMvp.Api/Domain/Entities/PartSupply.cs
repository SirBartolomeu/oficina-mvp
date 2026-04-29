using OficinaMvp.Api.Domain.Exceptions;

namespace OficinaMvp.Api.Domain.Entities;

public sealed class PartSupply
{
    private PartSupply()
    {
    }

    public PartSupply(string name, decimal unitPrice, int stockQuantity)
    {
        Id = Guid.NewGuid();
        Update(name, unitPrice, stockQuantity);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public int StockQuantity { get; private set; }

    public void Update(string name, decimal unitPrice, int stockQuantity)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("O nome da peça/insumo é obrigatório.");
        }

        if (unitPrice < 0)
        {
            throw new DomainException("O preço da peça/insumo não pode ser negativo.");
        }

        if (stockQuantity < 0)
        {
            throw new DomainException("O estoque não pode ser negativo.");
        }

        Name = name.Trim();
        UnitPrice = decimal.Round(unitPrice, 2, MidpointRounding.AwayFromZero);
        StockQuantity = stockQuantity;
    }

    public void RemoveFromStock(int quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainException("A quantidade deve ser maior que zero.");
        }

        if (StockQuantity < quantity)
        {
            throw new DomainException($"Estoque insuficiente para {Name}. Disponível: {StockQuantity}.");
        }

        StockQuantity -= quantity;
    }
}
