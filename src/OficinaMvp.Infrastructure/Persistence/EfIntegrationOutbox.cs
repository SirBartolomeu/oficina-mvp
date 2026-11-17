using System.Text.Json;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Application.Ports;

namespace OficinaMvp.Infrastructure.Persistence;

public sealed class EfIntegrationOutbox(WorkshopDbContext dbContext) : IIntegrationOutbox
{
    public void Enqueue<TPayload>(IntegrationEnvelope<TPayload> envelope)
    {
        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        dbContext.OutboxMessages.Add(new OutboxMessage(
            envelope.EventId, envelope.EventType, json, envelope.OccurredAtUtc));
    }
}
