using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Infrastructure.Persistence;
using OficinaMvp.Integration.Tests.Infrastructure;

namespace OficinaMvp.Integration.Tests;

public sealed class ClientTrackingAuthorizationTests
{
    [Fact]
    public async Task CustomerTokenOnlyOpensOwnOrderWhileActive()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        var customer = new Customer("Cliente Teste", "52998224725", null, null);
        var vehicle = new Vehicle(customer.Id, "BRA2E19", "VW", "Gol", 2020);
        var order = new WorkOrder(customer.Id, vehicle.Id,
            [new WorkOrderServiceLine(Guid.NewGuid(), "Revisão", 1, 125m)],
            [], null, DateTime.UtcNow, "sp-centro");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkshopDbContext>();
            db.AddRange(customer, vehicle, order);
            await db.SaveChangesAsync();
        }

        var url = $"/api/client/work-orders/{order.Id}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
        using (var wrongRequest = new HttpRequestMessage(HttpMethod.Get, url))
        {
            wrongRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                await CustomerTokenFactory.CreateAsync(Guid.NewGuid()));
            Assert.Equal(HttpStatusCode.NotFound,
                (await client.SendAsync(wrongRequest)).StatusCode);
        }
        var token = await CustomerTokenFactory.CreateAsync(customer.Id);
        using (var adminRequest = new HttpRequestMessage(HttpMethod.Get, "/api/customers"))
        {
            adminRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.SendAsync(adminRequest)).StatusCode);
        }
        using (var ownRequest = new HttpRequestMessage(HttpMethod.Get, url))
        {
            ownRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await client.SendAsync(ownRequest);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.Equal(order.Id, body.GetProperty("workOrderId").GetGuid());
            Assert.False(body.TryGetProperty("customerDocument", out _));
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkshopDbContext>();
            var saved = await db.Customers.SingleAsync(item => item.Id == customer.Id);
            saved.Deactivate();
            await db.SaveChangesAsync();
        }
        using var inactiveRequest = new HttpRequestMessage(HttpMethod.Get, url);
        inactiveRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.SendAsync(inactiveRequest)).StatusCode);
    }
}
