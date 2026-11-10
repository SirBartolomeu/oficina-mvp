using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Application.Ports;
using OficinaMvp.Integration.Tests.Infrastructure;

namespace OficinaMvp.Integration.Tests;

public sealed class RemoteExecutionQuoteTests
{
    [Fact]
    public async Task WorkOrderUsesRemoteCatalogSnapshotWithoutLocalCatalogRows()
    {
        var serviceId = Guid.NewGuid();
        var partId = Guid.NewGuid();
        using var factory = new CustomWebApplicationFactory
        {
            ExecutionQuoteClient = new StubQuoteClient((_, _) => new ExecutionQuoteResponse(
                [new ExecutionQuotedItem(serviceId, "Diagnóstico remoto", 1, 12500)],
                [new ExecutionQuotedItem(partId, "Filtro remoto", 2, 3250)],
                19000))
        };
        using var client = factory.CreateClient();
        await AuthenticateAndCreateCustomerAsync(client);

        var response = await client.PostAsJsonAsync("/api/work-orders", NewRequest(serviceId, partId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(190m, order.GetProperty("budgetTotal").GetDecimal());
        Assert.Equal("Diagnóstico remoto", order.GetProperty("services")[0].GetProperty("serviceName").GetString());
        Assert.Equal("Filtro remoto", order.GetProperty("parts")[0].GetProperty("partName").GetString());
    }

    [Fact]
    public async Task WorkOrderRejectsInconsistentRemoteQuote()
    {
        var serviceId = Guid.NewGuid();
        var partId = Guid.NewGuid();
        using var factory = new CustomWebApplicationFactory
        {
            ExecutionQuoteClient = new StubQuoteClient((_, _) => new ExecutionQuoteResponse(
                [new ExecutionQuotedItem(serviceId, "Serviço", 1, 10000)],
                [new ExecutionQuotedItem(partId, "Peça", 2, 2500)],
                9999))
        };
        using var client = factory.CreateClient();
        await AuthenticateAndCreateCustomerAsync(client);

        var response = await client.PostAsJsonAsync("/api/work-orders", NewRequest(serviceId, partId));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var orders = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/work-orders");
        Assert.Empty(orders.EnumerateArray());
    }

    [Fact]
    public async Task UnavailableExecutionReturnsGatewayErrorWithoutCreatingOrder()
    {
        using var factory = new CustomWebApplicationFactory
        {
            ExecutionQuoteClient = new FailingQuoteClient()
        };
        using var client = factory.CreateClient();
        await AuthenticateAndCreateCustomerAsync(client);

        var response = await client.PostAsJsonAsync("/api/work-orders", NewRequest(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var orders = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/work-orders");
        Assert.Empty(orders.EnumerateArray());
    }

    private static CreateWorkOrderRequest NewRequest(Guid serviceId, Guid partId) => new()
    {
        CustomerDocument = "52998224725",
        Vehicle = new VehicleInfo { LicensePlate = "BRA2E19", Brand = "VW", Model = "Gol", Year = 2020 },
        Services = [new RequestedService { ServiceId = serviceId, Quantity = 1 }],
        Parts = [new RequestedPart { PartId = partId, Quantity = 2 }]
    };

    private static async Task AuthenticateAndCreateCustomerAsync(HttpClient client)
    {
        var auth = await client.PostAsJsonAsync("/api/auth/token", new TokenRequest("admin", "Admin@123"));
        auth.EnsureSuccessStatusCode();
        var token = await auth.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        var customer = await client.PostAsJsonAsync("/api/customers",
            new UpsertCustomerRequest("Maria", "52998224725", "11999999999", "maria@example.com"));
        customer.EnsureSuccessStatusCode();
    }

    private sealed class StubQuoteClient(Func<IReadOnlyCollection<RequestedService>, IReadOnlyCollection<RequestedPart>, ExecutionQuoteResponse> quote)
        : IExecutionQuoteClient
    {
        public Task<ExecutionQuoteResponse> QuoteAsync(IReadOnlyCollection<RequestedService> services,
            IReadOnlyCollection<RequestedPart> parts, CancellationToken cancellationToken) =>
            Task.FromResult(quote(services, parts));
    }

    private sealed class FailingQuoteClient : IExecutionQuoteClient
    {
        public Task<ExecutionQuoteResponse> QuoteAsync(IReadOnlyCollection<RequestedService> services,
            IReadOnlyCollection<RequestedPart> parts, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Execution unavailable");
    }
}
