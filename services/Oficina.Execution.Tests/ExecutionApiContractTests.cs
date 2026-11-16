using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Oficina.Execution.Contracts;
using Xunit;

namespace Oficina.Execution.Tests;

public sealed class ExecutionApiContractTests
{
    [Fact]
    public async Task CatalogQuoteAndReservationFollowThePublicInternalContract()
    {
        const string token = "execution-api-contract-test-token-123456";
        var database = $"Data Source=execution-api-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(database);
        await anchor.OpenAsync();
        using var environment = new TestEnvironment(new Dictionary<string, string>
        {
            ["ConnectionStrings__DefaultConnection"] = database,
            ["Database__Provider"] = "Sqlite",
            ["InternalToken"] = token,
            ["Messaging__CommandsQueueUrl"] = "",
            ["Messaging__OsEventsQueueUrl"] = "",
            ["Observability__OtlpEndpoint"] = ""
        });
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/internal/catalog/services")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Internal-Token", token);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/internal/catalog/services", new RepairServiceRequest("", "", 125m, 30))).StatusCode);
        using var createdService = await client.PostAsJsonAsync("/internal/catalog/services",
            new RepairServiceRequest("Revisão", "Preventiva", 125m, 30));
        Assert.Equal(HttpStatusCode.Created, createdService.StatusCode);
        var serviceId = (await createdService.Content.ReadFromJsonAsync<CreatedItem>())!.Id;
        using var createdPart = await client.PostAsJsonAsync("/internal/catalog/parts",
            new PartSupplyRequest("Filtro", 30m, 3));
        Assert.Equal(HttpStatusCode.Created, createdPart.StatusCode);
        var partId = (await createdPart.Content.ReadFromJsonAsync<CreatedItem>())!.Id;
        Assert.Single((await client.GetFromJsonAsync<CreatedItem[]>("/internal/catalog/services"))!);
        Assert.Single((await client.GetFromJsonAsync<CreatedItem[]>("/internal/catalog/parts"))!);

        var quote = await client.PostAsJsonAsync("/internal/catalog/quote", new QuoteRequest(
            [new CatalogItemRequest(serviceId, 2)], [new CatalogItemRequest(partId, 1)]));
        Assert.Equal(HttpStatusCode.OK, quote.StatusCode);
        Assert.Equal(28000, (await quote.Content.ReadFromJsonAsync<QuoteResponse>())!.TotalCents);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/internal/catalog/quote", new QuoteRequest([], []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/internal/catalog/quote", new QuoteRequest(
                [new CatalogItemRequest(Guid.NewGuid(), 1)], []))).StatusCode);

        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var reservation = new ReserveStockRequest(reservationId, orderId,
            [new CatalogItemRequest(partId, 2)]);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            "/internal/stock/reservations", reservation)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            "/internal/stock/reservations", reservation)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/internal/stock/reservations", reservation with { WorkOrderId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/internal/stock/reservations", new ReserveStockRequest(Guid.NewGuid(), orderId,
                [new CatalogItemRequest(partId, 2)]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(
            $"/internal/stock/reservations/{reservationId}/release", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(
            $"/internal/stock/reservations/{reservationId}/release", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(
            $"/internal/stock/reservations/{Guid.NewGuid()}/release", null)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<object[]>("/internal/executions"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(
            $"/internal/executions/{Guid.NewGuid()}/finish", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(
            $"/internal/executions/{Guid.NewGuid()}/reject", new RejectRepairRequest("Sem peça"))).StatusCode);
    }

    private sealed record CreatedItem(Guid Id);

    private sealed class TestEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = new();

        public TestEnvironment(IReadOnlyDictionary<string, string> values)
        {
            foreach (var (key, value) in values)
            {
                _previous[key] = Environment.GetEnvironmentVariable(key);
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose()
        {
            foreach (var (key, value) in _previous)
                Environment.SetEnvironmentVariable(key, value);
        }
    }
}
