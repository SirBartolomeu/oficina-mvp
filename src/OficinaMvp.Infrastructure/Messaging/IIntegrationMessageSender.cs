namespace OficinaMvp.Infrastructure.Messaging;

public interface IIntegrationMessageSender
{
    Task SendAsync(Guid eventId, string eventType, string payloadJson, CancellationToken cancellationToken);
}
