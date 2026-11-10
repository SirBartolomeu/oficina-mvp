using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Infrastructure.Execution;

namespace OficinaMvp.Integration.Tests;

public sealed class HttpExecutionQuoteClientTests
{
    [Fact]
    public async Task SendsQuoteBatchWithInternalTokenAndReadsCents()
    {
        var serviceId = Guid.NewGuid();
        var partId = Guid.NewGuid();
        var handler = new CaptureHandler((request, body) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://execution/internal/catalog/quote", request.RequestUri!.ToString());
            Assert.Equal("local-token", request.Headers.GetValues("X-Internal-Token").Single());
            using var json = JsonDocument.Parse(body);
            Assert.Equal(serviceId, json.RootElement.GetProperty("services")[0].GetProperty("id").GetGuid());
            Assert.Equal(partId, json.RootElement.GetProperty("parts")[0].GetProperty("id").GetGuid());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ExecutionQuoteResponse(
                    [new ExecutionQuotedItem(serviceId, "Mão de obra", 1, 10025)],
                    [new ExecutionQuotedItem(partId, "Peça", 2, 1500)], 13025))
            };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://execution/") };
        var client = new HttpExecutionQuoteClient(http, "local-token");

        var quote = await client.QuoteAsync(
            [new RequestedService { ServiceId = serviceId, Quantity = 1 }],
            [new RequestedPart { PartId = partId, Quantity = 2 }], CancellationToken.None);

        Assert.Equal(13025, quote.TotalCents);
        Assert.Equal(10025, quote.Services.Single().UnitPriceCents);
    }

    private sealed class CaptureHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return respond(request, body);
        }
    }
}
