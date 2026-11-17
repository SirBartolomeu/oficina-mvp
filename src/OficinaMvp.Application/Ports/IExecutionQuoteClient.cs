using OficinaMvp.Application.Contracts;

namespace OficinaMvp.Application.Ports;

public interface IExecutionQuoteClient
{
    Task<ExecutionQuoteResponse> QuoteAsync(
        IReadOnlyCollection<RequestedService> services,
        IReadOnlyCollection<RequestedPart> parts,
        CancellationToken cancellationToken);
}
