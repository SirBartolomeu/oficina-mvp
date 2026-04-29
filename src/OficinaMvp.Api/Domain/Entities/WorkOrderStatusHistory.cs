using OficinaMvp.Api.Domain.Exceptions;

namespace OficinaMvp.Api.Domain.Entities;

public sealed class WorkOrderStatusHistory
{
    private WorkOrderStatusHistory()
    {
    }

    public WorkOrderStatusHistory(WorkOrderStatus status, DateTime changedAtUtc, string note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            throw new DomainException("A observação de status é obrigatória.");
        }

        Status = status;
        ChangedAtUtc = changedAtUtc;
        Note = note.Trim();
    }

    public Guid Id { get; private set; }
    public Guid WorkOrderId { get; private set; }
    public WorkOrderStatus Status { get; private set; }
    public DateTime ChangedAtUtc { get; private set; }
    public string Note { get; private set; } = string.Empty;
}
