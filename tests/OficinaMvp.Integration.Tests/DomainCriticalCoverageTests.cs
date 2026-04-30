using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using OficinaMvp.Api.Application.Contracts;
using OficinaMvp.Integration.Tests.Infrastructure;
using Xunit.Sdk;

namespace OficinaMvp.Integration.Tests;

public sealed class DomainCriticalCoverageTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _httpClient;

    public DomainCriticalCoverageTests(CustomWebApplicationFactory factory)
    {
        _httpClient = factory.CreateClient();
    }

    [Fact]
    public async Task ShouldReturnBadRequest_WhenCreatingCustomerWithInvalidDocument()
    {
        await AuthenticateAsync();

        var response = await _httpClient.PostAsJsonAsync(
            "/api/customers",
            new UpsertCustomerRequest("Cliente Invalido", "11111111111", "11999999999", "cliente@email.com"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ShouldReturnBadRequest_WhenCreatingVehicleWithInvalidPlate()
    {
        await AuthenticateAsync();
        var customerDocument = GenerateValidCpf();
        var customerId = await PostAndGetCreatedIdAsync(
            "/api/customers",
            new UpsertCustomerRequest("Maria", customerDocument, "11999999999", "maria@email.com"));

        var response = await _httpClient.PostAsJsonAsync(
            "/api/vehicles",
            new UpsertVehicleRequest(customerId, "XXX1", "Volkswagen", "Gol", 2020));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ShouldReturnBadRequest_WhenStockIsInsufficientForWorkOrder()
    {
        await AuthenticateAsync();
        var customerDocument = GenerateValidCpf();

        await PostAndGetCreatedIdAsync(
            "/api/customers",
            new UpsertCustomerRequest("Carlos", customerDocument, "11999999999", "carlos@email.com"));

        var serviceId = await PostAndGetCreatedIdAsync(
            "/api/services",
            new UpsertRepairServiceRequest("Troca de oleo", "Troca completa", 100m, 60));

        var partId = await PostAndGetCreatedIdAsync(
            "/api/parts",
            new UpsertPartSupplyRequest("Filtro", 20m, 1));

        var response = await _httpClient.PostAsJsonAsync(
            "/api/work-orders",
            new CreateWorkOrderRequest
            {
                CustomerDocument = customerDocument,
                Vehicle = new VehicleInfo
                {
                    LicensePlate = "ABC1234",
                    Brand = "Volkswagen",
                    Model = "Gol",
                    Year = 2020
                },
                Services = new[] { new RequestedService { ServiceId = serviceId, Quantity = 1 } },
                Parts = new[] { new RequestedPart { PartId = partId, Quantity = 2 } }
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ShouldReturnBadRequest_WhenWorkOrderTransitionIsInvalid()
    {
        await AuthenticateAsync();
        var workOrderId = await CreateWorkOrderAsync(
            customerDocument: GenerateValidCpf(),
            customerName: "Fernanda",
            plate: "BRA2E19");

        var response = await _httpClient.PostAsJsonAsync($"/api/work-orders/{workOrderId}/deliver", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ShouldReturnBadRequest_WhenClientTrackingDocumentIsInvalid()
    {
        await AuthenticateAsync();
        var workOrderId = await CreateWorkOrderAsync(
            customerDocument: GenerateValidCpf(),
            customerName: "Roberto",
            plate: "DEF5678");

        var response = await _httpClient.GetAsync($"/api/client/work-orders/{workOrderId}?document=123");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task AuthenticateAsync()
    {
        var authResponse = await _httpClient.PostAsJsonAsync("/api/auth/token", new TokenRequest("admin", "Admin@123"));
        authResponse.EnsureSuccessStatusCode();

        var token = await authResponse.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(token);

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
    }

    private async Task<Guid> CreateWorkOrderAsync(string customerDocument, string customerName, string plate)
    {
        await PostAndGetCreatedIdAsync(
            "/api/customers",
            new UpsertCustomerRequest(customerName, customerDocument, "11999999999", $"{customerName.ToLowerInvariant()}@email.com"));

        var serviceId = await PostAndGetCreatedIdAsync(
            "/api/services",
            new UpsertRepairServiceRequest("Alinhamento", "Alinhamento tecnico", 80m, 45));

        var partId = await PostAndGetCreatedIdAsync(
            "/api/parts",
            new UpsertPartSupplyRequest("Parafuso", 5m, 10));

        var response = await _httpClient.PostAsJsonAsync(
            "/api/work-orders",
            new CreateWorkOrderRequest
            {
                CustomerDocument = customerDocument,
                Vehicle = new VehicleInfo
                {
                    LicensePlate = plate,
                    Brand = "Fiat",
                    Model = "Uno",
                    Year = 2018
                },
                Services = new[] { new RequestedService { ServiceId = serviceId, Quantity = 1 } },
                Parts = new[] { new RequestedPart { PartId = partId, Quantity = 1 } },
                Notes = "Criacao para cenarios de validacao."
            });

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new XunitException($"Create work-order failed: {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }

        return ExtractIdFromLocationHeader(response);
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
