using OficinaMvp.Application.Contracts;

namespace OficinaMvp.Application.Ports;

public interface IIntegrationOutbox
{
    void Enqueue<TPayload>(IntegrationEnvelope<TPayload> envelope);
}
