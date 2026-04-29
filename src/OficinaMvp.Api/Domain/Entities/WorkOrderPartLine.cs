using OficinaMvp.Api.Domain.Exceptions;

namespace OficinaMvp.Api.Domain.Entities;

public sealed class WorkOrderPartLine
{
    private WorkOrderPartLine()
    {
    }

    public WorkOrderPartLine(Guid partSupplyId, string partName, int quantity, decimal unitPrice)
    {
        PartSupplyId = partSupplyId;
        Update(partName, quantity, unitPrice);
    }

    public Guid Id { get; private set; }
    public Guid WorkOrderId { get; private set; }
    public Guid PartSupplyId { get; private set; }
    public string PartName { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal Subtotal => decimal.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);

    private void Update(string partName, int quantity, decimal unitPrice)
    {
        if (string.IsNullOrWhiteSpace(partName))
        {
            throw new DomainException("O nome da peça/insumo na OS é obrigatório.");
        }

        if (quantity <= 0)
        {
            throw new DomainException("A quantidade de peças/insumos deve ser maior que zero.");
        }

        if (unitPrice < 0)
        {
            throw new DomainException("O valor da peça/insumo não pode ser negativo.");
        }

        PartName = partName.Trim();
        Quantity = quantity;
        UnitPrice = decimal.Round(unitPrice, 2, MidpointRounding.AwayFromZero);
    }
}
