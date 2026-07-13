using OficinaMvp.Domain.Entities;

namespace OficinaMvp.Application.Ports;

public sealed record WorkOrderStatusNotification(
    Guid WorkOrderId,
    Guid CustomerId,
    WorkOrderStatus Status,
    string Note,
    DateTime ChangedAtUtc);

public interface IWorkOrderStatusNotifier
{
    Task NotifyAsync(WorkOrderStatusNotification notification, CancellationToken cancellationToken);
}
