using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Integration.Tests.Infrastructure;

namespace OficinaMvp.Integration.Tests;

public sealed class CatalogCrudTests
{
    [Fact]
    public async Task CustomerCanBeCreatedUpdatedListedAndDeactivated()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        var missingId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/customers/{missingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/customers/{missingId}",
            new UpsertCustomerRequest("Teste", "52998224725", null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/customers/{missingId}")).StatusCode);

        var created = await client.PostAsJsonAsync("/api/customers",
            new UpsertCustomerRequest("Maria Inicial", "52998224725", "11999999999", "maria@example.com"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var customer = await created.Content.ReadFromJsonAsync<CustomerResponse>();
        Assert.NotNull(customer);
        Assert.True(customer.IsActive);

        var listed = await client.GetFromJsonAsync<List<CustomerResponse>>("/api/customers");
        Assert.Contains(listed!, item => item.Id == customer.Id);
        var updated = await client.PutAsJsonAsync($"/api/customers/{customer.Id}",
            new UpsertCustomerRequest("Maria Atualizada", "52998224725", null, null));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Maria Atualizada", (await client.GetFromJsonAsync<CustomerResponse>(
            $"/api/customers/{customer.Id}"))!.Name);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/customers/{customer.Id}")).StatusCode);
        Assert.False((await client.GetFromJsonAsync<CustomerResponse>($"/api/customers/{customer.Id}"))!.IsActive);
    }

    [Fact]
    public async Task VehicleRequiresExistingCustomerAndCannotChangeOwner()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        var missingId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/vehicles/{missingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/vehicles/{missingId}",
            new UpsertVehicleRequest(Guid.NewGuid(), "BRA2E19", "VW", "Gol", 2020))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/vehicles/{missingId}")).StatusCode);

        var unknownOwner = await client.PostAsJsonAsync("/api/vehicles",
            new UpsertVehicleRequest(Guid.NewGuid(), "BRA2E19", "VW", "Gol", 2020));
        Assert.Equal(HttpStatusCode.NotFound, unknownOwner.StatusCode);

        var ownerResponse = await client.PostAsJsonAsync("/api/customers",
            new UpsertCustomerRequest("Dono", "52998224725", null, null));
        ownerResponse.EnsureSuccessStatusCode();
        var owner = (await ownerResponse.Content.ReadFromJsonAsync<CustomerResponse>())!;
        var created = await client.PostAsJsonAsync("/api/vehicles",
            new UpsertVehicleRequest(owner.Id, "BRA2E19", "VW", "Gol", 2020));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var vehicle = (await created.Content.ReadFromJsonAsync<VehicleResponse>())!;
        Assert.Equal(owner.Id, vehicle.CustomerId);
        Assert.Contains((await client.GetFromJsonAsync<List<VehicleResponse>>("/api/vehicles"))!,
            item => item.Id == vehicle.Id);

        var changeOwner = await client.PutAsJsonAsync($"/api/vehicles/{vehicle.Id}",
            new UpsertVehicleRequest(Guid.NewGuid(), "BRA2E19", "VW", "Gol", 2020));
        Assert.Equal(HttpStatusCode.BadRequest, changeOwner.StatusCode);
        var update = await client.PutAsJsonAsync($"/api/vehicles/{vehicle.Id}",
            new UpsertVehicleRequest(owner.Id, "BRA2E19", "Volkswagen", "Polo", 2021));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("Polo", (await client.GetFromJsonAsync<VehicleResponse>(
            $"/api/vehicles/{vehicle.Id}"))!.Model);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/vehicles/{vehicle.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/vehicles/{vehicle.Id}")).StatusCode);
    }

    [Fact]
    public async Task RepairServiceCanBeCreatedUpdatedListedAndRemoved()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        var missingId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/services/{missingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/services/{missingId}",
            new UpsertRepairServiceRequest("Revisão", "Inspeção", 120m, 45))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/services/{missingId}")).StatusCode);

        var created = await client.PostAsJsonAsync("/api/services",
            new UpsertRepairServiceRequest("Revisão", "Inspeção", 120m, 45));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var service = (await created.Content.ReadFromJsonAsync<RepairServiceResponse>())!;
        Assert.Equal(120m, service.LaborPrice);
        Assert.Contains((await client.GetFromJsonAsync<List<RepairServiceResponse>>("/api/services"))!,
            item => item.Id == service.Id);

        var updated = await client.PutAsJsonAsync($"/api/services/{service.Id}",
            new UpsertRepairServiceRequest("Revisão completa", "Inspeção e ajuste", 180m, 60));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var saved = await client.GetFromJsonAsync<RepairServiceResponse>($"/api/services/{service.Id}");
        Assert.Equal("Revisão completa", saved!.Name);
        Assert.Equal(180m, saved.LaborPrice);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/services/{service.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/services/{service.Id}")).StatusCode);
    }

    [Fact]
    public async Task PartCanBeCreatedUpdatedListedAndRemoved()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        var missingId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/parts/{missingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/parts/{missingId}",
            new UpsertPartSupplyRequest("Filtro", 30m, 4))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/parts/{missingId}")).StatusCode);

        var created = await client.PostAsJsonAsync("/api/parts",
            new UpsertPartSupplyRequest("Filtro", 30m, 4));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var part = (await created.Content.ReadFromJsonAsync<PartSupplyResponse>())!;
        Assert.Equal(4, part.StockQuantity);
        Assert.Contains((await client.GetFromJsonAsync<List<PartSupplyResponse>>("/api/parts"))!,
            item => item.Id == part.Id);

        var updated = await client.PutAsJsonAsync($"/api/parts/{part.Id}",
            new UpsertPartSupplyRequest("Filtro premium", 45m, 8));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var saved = await client.GetFromJsonAsync<PartSupplyResponse>($"/api/parts/{part.Id}");
        Assert.Equal("Filtro premium", saved!.Name);
        Assert.Equal(8, saved.StockQuantity);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/parts/{part.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/parts/{part.Id}")).StatusCode);
    }

    private static async Task AuthenticateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/token", new TokenRequest("admin", "Admin@123"));
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
    }
}
