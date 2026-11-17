using OficinaMvp.Infrastructure.Messaging;

namespace OficinaMvp.Infrastructure.Persistence;

public sealed class OutboxMessage
{
    private OutboxMessage() { }

    public OutboxMessage(Guid eventId, string eventType, string payloadJson, DateTime occurredAtUtc)
    {
        EventId = eventId;
        EventType = eventType;
        PayloadJson = SqsTraceContext.Stamp(payloadJson);
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public DateTime? NextAttemptAtUtc { get; private set; }
    public int PublishAttempts { get; private set; }
    public string? LastError { get; private set; }

    public void MarkPublished(DateTime publishedAtUtc)
    {
        PublishedAtUtc = publishedAtUtc;
        NextAttemptAtUtc = null;
        LastError = null;
        PublishAttempts++;
    }

    public void MarkFailed(string error, DateTime failedAtUtc)
    {
        PublishAttempts++;
        LastError = error.Length <= 500 ? error : error[..500];
        NextAttemptAtUtc = failedAtUtc.AddSeconds(
            Math.Min(300, 1 << Math.Min(PublishAttempts, 9)));
    }
}
