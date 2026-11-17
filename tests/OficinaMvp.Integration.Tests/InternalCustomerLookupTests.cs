using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Infrastructure.Persistence;
using OficinaMvp.Integration.Tests.Infrastructure;

namespace OficinaMvp.Integration.Tests;

public sealed class InternalCustomerLookupTests
{
    [Fact]
    public async Task LookupRequiresServiceTokenAndReturnsActualCustomerStatus()
    {
        const string token = "local-lookup-token-with-at-least-32-characters";
        using var baseFactory = new CustomWebApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:LookupToken"] = token
                })));
        using var client = factory.CreateClient();
        var active = new Customer("Cliente Ativo", "52998224725", null, null);
        var inactive = new Customer("Cliente Inativo", "11144477735", null, null);
        inactive.Deactivate();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkshopDbContext>();
            db.Customers.AddRange(active, inactive);
            await db.SaveChangesAsync();
        }

        const string url = "/internal/customers/lookup";
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(url, new { cpf = active.Document })).StatusCode);
        client.DefaultRequestHeaders.Add("X-Internal-Token", "wrong-token");
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(url, new { cpf = active.Document })).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Internal-Token");
        client.DefaultRequestHeaders.Add("X-Internal-Token", token);

        var activeResponse = await client.PostAsJsonAsync(url, new { cpf = "529.982.247-25" });
        Assert.Equal(HttpStatusCode.OK, activeResponse.StatusCode);
        var activeLookup = await activeResponse.Content.ReadFromJsonAsync<CustomerLookup>();
        Assert.Equal(active.Id, activeLookup!.CustomerId);
        Assert.True(activeLookup.IsActive);

        var inactiveResponse = await client.PostAsJsonAsync(url, new { cpf = inactive.Document });
        Assert.Equal(HttpStatusCode.OK, inactiveResponse.StatusCode);
        var inactiveLookup = await inactiveResponse.Content.ReadFromJsonAsync<CustomerLookup>();
        Assert.Equal(inactive.Id, inactiveLookup!.CustomerId);
        Assert.False(inactiveLookup.IsActive);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync(url, new { cpf = "000.000.000-00" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync(url, new { cpf = "04.252.011/0001-10" })).StatusCode);
        using var verificationScope = factory.Services.CreateScope();
        Assert.Equal(2, await verificationScope.ServiceProvider.GetRequiredService<WorkshopDbContext>()
            .Customers.CountAsync());
    }

    private sealed record CustomerLookup(Guid CustomerId, bool IsActive);
}
