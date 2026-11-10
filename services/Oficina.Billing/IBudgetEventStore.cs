namespace Oficina.Billing;

public interface IBudgetEventStore
{
    Task<Budget> CreateFromEventAsync(Guid eventId, Budget proposed, CancellationToken cancellationToken);
}
