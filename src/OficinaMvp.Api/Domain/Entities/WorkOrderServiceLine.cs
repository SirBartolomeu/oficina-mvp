using OficinaMvp.Api.Domain.Exceptions;

namespace OficinaMvp.Api.Domain.Entities;

public sealed class WorkOrderServiceLine
{
    private WorkOrderServiceLine()
    {
    }

    public WorkOrderServiceLine(Guid serviceId, string serviceName, int quantity, decimal unitPrice)
    {
        ServiceId = serviceId;
        Update(serviceName, quantity, unitPrice);
    }

    public Guid Id { get; private set; }
    public Guid WorkOrderId { get; private set; }
    public Guid ServiceId { get; private set; }
    public string ServiceName { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal Subtotal => decimal.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);

    private void Update(string serviceName, int quantity, decimal unitPrice)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new DomainException("O nome do serviço na OS é obrigatório.");
        }

        if (quantity <= 0)
        {
            throw new DomainException("A quantidade de serviços deve ser maior que zero.");
        }

        if (unitPrice < 0)
        {
            throw new DomainException("O valor do serviço não pode ser negativo.");
        }

        ServiceName = serviceName.Trim();
        Quantity = quantity;
        UnitPrice = decimal.Round(unitPrice, 2, MidpointRounding.AwayFromZero);
    }
}
