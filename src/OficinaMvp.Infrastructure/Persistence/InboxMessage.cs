namespace OficinaMvp.Infrastructure.Persistence;

public sealed class InboxMessage
{
    private InboxMessage() { }

    public InboxMessage(Guid eventId, string eventType, DateTime receivedAtUtc)
    {
        EventId = eventId;
        EventType = eventType;
        ReceivedAtUtc = receivedAtUtc;
    }

    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; private set; }
}
