using System.Net.Http.Json;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Application.Ports;

namespace OficinaMvp.Infrastructure.Execution;

public sealed class HttpExecutionQuoteClient(HttpClient httpClient, string internalToken) : IExecutionQuoteClient
{
    public async Task<ExecutionQuoteResponse> QuoteAsync(
        IReadOnlyCollection<RequestedService> services,
        IReadOnlyCollection<RequestedPart> parts,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            Services = services.Select(item => new { Id = item.ServiceId, item.Quantity }),
            Parts = parts.Select(item => new { Id = item.PartId, item.Quantity })
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "internal/catalog/quote")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-Internal-Token", internalToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ExecutionQuoteResponse>(cancellationToken)
            ?? throw new InvalidOperationException("A Execução retornou uma cotação vazia.");
    }
}
