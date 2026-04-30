using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OficinaMvp.Api.Application.Contracts;
using OficinaMvp.Integration.Tests.Infrastructure;
using Xunit.Sdk;

namespace OficinaMvp.Integration.Tests;

public sealed class WorkOrderFlowTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _httpClient;

    public WorkOrderFlowTests(CustomWebApplicationFactory factory)
    {
        _httpClient = factory.CreateClient();
    }

    [Fact]
    public async Task AdminEndpoints_ShouldRequireAuthentication()
    {
        var response = await _httpClient.GetAsync("/api/customers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ShouldCreateAndTrackWorkOrder_WithStatusTransitionsAndStockControl()
    {
        await AuthenticateAsync();
        const string customerDocument = "52998224725";

        await PostAndGetCreatedIdAsync(
            "/api/customers",
            new UpsertCustomerRequest("João da Silva", "529.982.247-25", "11999999999", "joao@email.com"));

        var serviceId = await PostAndGetCreatedIdAsync(
            "/api/services",
            new UpsertRepairServiceRequest("Troca de óleo", "Troca de óleo e filtros", 150m, 60));

        var partId = await PostAndGetCreatedIdAsync(
            "/api/parts",
            new UpsertPartSupplyRequest("Filtro de óleo", 30m, 10));

        var createWorkOrderRequest = new CreateWorkOrderRequest
        {
            CustomerDocument = customerDocument,
            Vehicle = new VehicleInfo
            {
                LicensePlate = "BRA2E19",
                Brand = "Volkswagen",
                Model = "Gol",
                Year = 2021
            },
            Services = new[] { new RequestedService { ServiceId = serviceId, Quantity = 1 } },
            Parts = new[] { new RequestedPart { PartId = partId, Quantity = 2 } },
            Notes = "Revisão de 10 mil km"
        };

        var createdWorkOrder = await PostAndReadJsonAsync(
            "/api/work-orders",
            createWorkOrderRequest);

        var createdId = createdWorkOrder.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("Received", createdWorkOrder.RootElement.GetProperty("status").GetString());
        Assert.Equal(210m, createdWorkOrder.RootElement.GetProperty("budgetTotal").GetDecimal());

        var afterDiagnosis = await PostAndReadJsonAsync($"/api/work-orders/{createdId}/start-diagnosis");
        Assert.Equal("InDiagnosis", afterDiagnosis.RootElement.GetProperty("status").GetString());

        var afterBudget = await PostAndReadJsonAsync($"/api/work-orders/{createdId}/send-budget");
        Assert.Equal("AwaitingApproval", afterBudget.RootElement.GetProperty("status").GetString());

        var afterApprove = await PostAndReadJsonAsync($"/api/work-orders/{createdId}/approve-budget");
        Assert.Equal("InExecution", afterApprove.RootElement.GetProperty("status").GetString());

        var afterFinalize = await PostAndReadJsonAsync($"/api/work-orders/{createdId}/finalize");
        Assert.Equal("Finalized", afterFinalize.RootElement.GetProperty("status").GetString());

        var afterDeliver = await PostAndReadJsonAsync($"/api/work-orders/{createdId}/deliver");
        Assert.Equal("Delivered", afterDeliver.RootElement.GetProperty("status").GetString());
        Assert.Equal(6, afterDeliver.RootElement.GetProperty("statusHistory").GetArrayLength());

        var trackingResponse = await _httpClient.GetAsync($"/api/client/work-orders/{createdId}?document={customerDocument}");
        trackingResponse.EnsureSuccessStatusCode();
        var trackingPayload = await trackingResponse.Content.ReadAsStringAsync();
        using var tracking = JsonDocument.Parse(trackingPayload);

        Assert.Equal("Delivered", tracking.RootElement.GetProperty("status").GetString());
        Assert.Contains(
            tracking.RootElement.GetProperty("statusHistory").EnumerateArray().Select(item => item.GetProperty("status").GetString()),
            status => status == "AwaitingApproval");

        var updatedPartResponse = await _httpClient.GetAsync($"/api/parts/{partId}");
        updatedPartResponse.EnsureSuccessStatusCode();
        var updatedPartPayload = await updatedPartResponse.Content.ReadAsStringAsync();
        using var updatedPart = JsonDocument.Parse(updatedPartPayload);
        Assert.Equal(8, updatedPart.RootElement.GetProperty("stockQuantity").GetInt32());
    }

    private async Task AuthenticateAsync()
    {
        var authResponse = await _httpClient.PostAsJsonAsync("/api/auth/token", new TokenRequest("admin", "Admin@123"));
        authResponse.EnsureSuccessStatusCode();

        var token = await authResponse.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(token);

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
    }

    private async Task<JsonDocument> PostAndReadJsonAsync<TRequest>(string url, TRequest payload)
    {
        var response = await _httpClient.PostAsJsonAsync(url, payload);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new XunitException($"Request {url} failed: {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }

        var payloadJson = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(payloadJson);
    }

    private async Task<JsonDocument> PostAndReadJsonAsync(string url)
    {
        var response = await _httpClient.PostAsJsonAsync(url, new { });
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new XunitException($"Request {url} failed: {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }

        var payloadJson = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(payloadJson);
    }

    private async Task<Guid> PostAndGetCreatedIdAsync<TRequest>(string url, TRequest payload)
    {
        var response = await _httpClient.PostAsJsonAsync(url, payload);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new XunitException($"Request {url} failed: {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }

        var location = response.Headers.Location?.ToString()
                       ?? throw new XunitException($"Request {url} did not return a Location header.");

        var idSegment = location.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
        if (!Guid.TryParse(idSegment, out var id))
        {
            throw new XunitException($"Could not parse GUID from Location header: {location}");
        }

        return id;
    }
}
