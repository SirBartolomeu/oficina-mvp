using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Integration.Tests.Infrastructure;
using Xunit.Sdk;

namespace OficinaMvp.Integration.Tests;

public sealed class Phase2WorkOrderRequirementsTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string IntegrationToken = "oficina-integration-token-local-123456";
    private readonly HttpClient _httpClient;

    public Phase2WorkOrderRequirementsTests(CustomWebApplicationFactory factory)
    {
        _httpClient = factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpoints_ShouldBePublicAndReturnHealthy()
    {
        var live = await _httpClient.GetAsync("/health/live");
        var ready = await _httpClient.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task WorkOrderList_ShouldReturnOnlyActiveOrdersByRequiredPriorityAndOldestFirst()
    {
        await AuthenticateAsync();
        var serviceId = await CreateServiceAsync("Servico fase 2 listagem");

        var receivedOldId = await CreateWorkOrderAsync(serviceId, "Cliente Recebida 1", "BRA2E19");
        var receivedNewId = await CreateWorkOrderAsync(serviceId, "Cliente Recebida 2", "DEF5678");
        var diagnosisId = await CreateWorkOrderAsync(serviceId, "Cliente Diagnostico", "ABC1234");
        var awaitingId = await CreateWorkOrderAsync(serviceId, "Cliente Aguardando", "GHI9012");
        var executionId = await CreateWorkOrderAsync(serviceId, "Cliente Execucao", "JKL3456");
        var deliveredId = await CreateWorkOrderAsync(serviceId, "Cliente Entregue", "MNO7890");

        await PostAndReadJsonAsync($"/api/work-orders/{diagnosisId}/start-diagnosis");
        await PostAndReadJsonAsync($"/api/work-orders/{awaitingId}/send-budget");
        await PostAndReadJsonAsync($"/api/work-orders/{executionId}/send-budget");
        await PostAndReadJsonAsync($"/api/work-orders/{executionId}/approve-budget");
        await PostAndReadJsonAsync($"/api/work-orders/{deliveredId}/send-budget");
        await PostAndReadJsonAsync($"/api/work-orders/{deliveredId}/approve-budget");
        await PostAndReadJsonAsync($"/api/work-orders/{deliveredId}/finalize");
        await PostAndReadJsonAsync($"/api/work-orders/{deliveredId}/deliver");

        var response = await _httpClient.GetAsync("/api/work-orders");
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = payload.RootElement.EnumerateArray().ToList();
        var ids = items.Select(item => item.GetProperty("id").GetGuid()).ToList();
        var statuses = items.Select(item => item.GetProperty("status").GetString()).ToList();

        Assert.DoesNotContain(deliveredId, ids);
        Assert.True(ids.IndexOf(executionId) < ids.IndexOf(awaitingId));
        Assert.True(ids.IndexOf(awaitingId) < ids.IndexOf(diagnosisId));
        Assert.True(ids.IndexOf(diagnosisId) < ids.IndexOf(receivedOldId));
        Assert.True(ids.IndexOf(receivedOldId) < ids.IndexOf(receivedNewId));
        Assert.Contains("InExecution", statuses);
        Assert.Contains("AwaitingApproval", statuses);
        Assert.Contains("InDiagnosis", statuses);
        Assert.Contains("Received", statuses);
    }

    [Fact]
    public async Task StatusEndpoint_ShouldReturnCurrentStatusWithoutFullTrackingPayload()
    {
        await AuthenticateAsync();
        var serviceId = await CreateServiceAsync("Servico fase 2 status");
        var workOrderId = await CreateWorkOrderAsync(serviceId, "Cliente Status", "PQR1234");

        await PostAndReadJsonAsync($"/api/work-orders/{workOrderId}/start-diagnosis");

        var response = await _httpClient.GetAsync($"/api/work-orders/{workOrderId}/status");
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(workOrderId, payload.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("InDiagnosis", payload.RootElement.GetProperty("status").GetString());
        Assert.True(payload.RootElement.TryGetProperty("updatedAtUtc", out _));
        Assert.False(payload.RootElement.TryGetProperty("statusHistory", out _));
    }

    [Fact]
    public async Task ExternalBudgetDecision_ShouldRequireTokenAndSupportRefusalThenApproval()
    {
        await AuthenticateAsync();
        var serviceId = await CreateServiceAsync("Servico fase 2 budget externo");
        var workOrderId = await CreateWorkOrderAsync(serviceId, "Cliente Budget", "STU5678");
        await PostAndReadJsonAsync($"/api/work-orders/{workOrderId}/send-budget");

        var withoutToken = await _httpClient.PostAsJsonAsync(
            $"/api/integrations/work-orders/{workOrderId}/budget-decision",
            new BudgetDecisionRequest(false, "Cliente pediu revisao", null));
        Assert.Equal(HttpStatusCode.Unauthorized, withoutToken.StatusCode);

        using var refused = await PostIntegrationDecisionAsync(workOrderId, approved: false, reason: "Valor acima do esperado");
        Assert.Equal("AwaitingApproval", refused.RootElement.GetProperty("status").GetString());
        Assert.Contains(
            refused.RootElement.GetProperty("statusHistory").EnumerateArray(),
            item => item.GetProperty("note").GetString()!.Contains("recusou", StringComparison.OrdinalIgnoreCase));

        using var approved = await PostIntegrationDecisionAsync(workOrderId, approved: true, reason: null);
        Assert.Equal("InExecution", approved.RootElement.GetProperty("status").GetString());
    }

    private async Task AuthenticateAsync()
    {
        var authResponse = await _httpClient.PostAsJsonAsync("/api/auth/token", new TokenRequest("admin", "Admin@123"));
        authResponse.EnsureSuccessStatusCode();

        var token = await authResponse.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(token);

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
    }

    private async Task<Guid> CreateServiceAsync(string name) =>
        await PostAndGetCreatedIdAsync(
            "/api/services",
            new UpsertRepairServiceRequest(name, "Servico criado para teste de fase 2", 100m, 60));

    private async Task<Guid> CreateWorkOrderAsync(Guid serviceId, string customerName, string plate)
    {
        var document = GenerateValidCpf();
        await PostAndGetCreatedIdAsync(
            "/api/customers",
            new UpsertCustomerRequest(customerName, document, "11999999999", $"{Guid.NewGuid():N}@email.com"));

        var response = await _httpClient.PostAsJsonAsync(
            "/api/work-orders",
            new CreateWorkOrderRequest
            {
                CustomerDocument = document,
                Vehicle = new VehicleInfo
                {
                    LicensePlate = plate,
                    Brand = "Fiat",
                    Model = "Uno",
                    Year = 2020
                },
                Services = new[] { new RequestedService { ServiceId = serviceId, Quantity = 1 } }
            });

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new XunitException($"Create work-order failed: {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }

        return ExtractIdFromLocationHeader(response);
    }

    private async Task<JsonDocument> PostIntegrationDecisionAsync(Guid workOrderId, bool approved, string? reason)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/integrations/work-orders/{workOrderId}/budget-decision")
        {
            Content = JsonContent.Create(new BudgetDecisionRequest(approved, reason, null))
        };
        request.Headers.Add("X-Integration-Token", IntegrationToken);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new XunitException($"Integration decision failed: {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private async Task<JsonDocument> PostAndReadJsonAsync(string url)
    {
        var response = await _httpClient.PostAsJsonAsync(url, new { });
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new XunitException($"Request {url} failed: {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> PostAndGetCreatedIdAsync<TRequest>(string url, TRequest payload)
    {
        var response = await _httpClient.PostAsJsonAsync(url, payload);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new XunitException($"Request {url} failed: {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }

        return ExtractIdFromLocationHeader(response);
    }

    private static Guid ExtractIdFromLocationHeader(HttpResponseMessage response)
    {
        var location = response.Headers.Location?.ToString()
                       ?? throw new XunitException("Response did not return a Location header.");

        var idSegment = location.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
        if (!Guid.TryParse(idSegment, out var id))
        {
            throw new XunitException($"Could not parse GUID from Location header: {location}");
        }

        return id;
    }

    private static string GenerateValidCpf()
    {
        var random = Guid.NewGuid().ToByteArray();
        var digits = new int[11];
        for (var index = 0; index < 9; index++)
        {
            digits[index] = random[index] % 10;
        }

        digits[9] = CalculateDigit(digits, 9, 10);
        digits[10] = CalculateDigit(digits, 10, 11);

        return string.Concat(digits.Select(number => number.ToString()));
    }

    private static int CalculateDigit(int[] digits, int length, int weightStart)
    {
        var sum = 0;
        for (var index = 0; index < length; index++)
        {
            sum += digits[index] * (weightStart - index);
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
