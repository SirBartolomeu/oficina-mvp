using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oficina.Auth;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Infrastructure.Persistence;
using OficinaMvp.Integration.Tests.Infrastructure;

namespace OficinaMvp.Integration.Tests;

public sealed class CustomerAuthFlowTests
{
    [Fact]
    public async Task CpfLookupIssuesTokenThatTracksOnlyCustomersOrder()
    {
        const string lookupToken = "local-lookup-token-with-at-least-32-characters";
        using var baseFactory = new CustomWebApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:LookupToken"] = lookupToken
                })));
        using var osClient = factory.CreateClient();
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

        using var lookupHttp = new HttpClient(new ForwardLookupHandler(osClient));
        var function = new Function(new CustomerTokenService(lookupHttp, new AuthOptions(
            new Uri("http://os.local/internal/customers/lookup"), lookupToken,
            "OficinaMvp-ChaveJWT-SuperSecreta-Com-32-Caracteres",
            "OficinaMvp", "OficinaMvpClients")));
        var gatewayEvent = JsonSerializer.Serialize(new
        {
            requestContext = new { http = new { method = "POST" } },
            body = JsonSerializer.Serialize(new { cpf = "529.982.247-25" }),
            isBase64Encoded = false
        });
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(gatewayEvent));
        using var result = await function.FunctionHandler(input);
        using var gatewayResponse = await JsonDocument.ParseAsync(result);
        Assert.Equal(200, gatewayResponse.RootElement.GetProperty("statusCode").GetInt32());
        using var tokenBody = JsonDocument.Parse(gatewayResponse.RootElement
            .GetProperty("body").GetString()!);
        var token = tokenBody.RootElement.GetProperty("accessToken").GetString();
        Assert.NotNull(token);

        using var trackingRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/api/client/work-orders/{order.Id}");
        trackingRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var trackingResponse = await osClient.SendAsync(trackingRequest);
        Assert.Equal(HttpStatusCode.OK, trackingResponse.StatusCode);
        using var tracking = JsonDocument.Parse(await trackingResponse.Content.ReadAsStringAsync());
        Assert.Equal(order.Id, tracking.RootElement.GetProperty("workOrderId").GetGuid());
        Assert.False(tracking.RootElement.TryGetProperty("customerDocument", out _));

        using var adminLogin = await osClient.PostAsJsonAsync("/api/auth/token",
            new { username = "admin", password = "Admin@123" });
        adminLogin.EnsureSuccessStatusCode();
        using var adminToken = JsonDocument.Parse(await adminLogin.Content.ReadAsStringAsync());
        using var deactivate = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/customers/{customer.Id}");
        deactivate.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            adminToken.RootElement.GetProperty("accessToken").GetString());
        using var deactivateResponse = await osClient.SendAsync(deactivate);
        Assert.Equal(HttpStatusCode.NoContent, deactivateResponse.StatusCode);

        using var scopeAfter = factory.Services.CreateScope();
        var saved = await scopeAfter.ServiceProvider.GetRequiredService<WorkshopDbContext>()
            .Customers.FindAsync(customer.Id);
        Assert.NotNull(saved);
        Assert.False(saved.IsActive);

        using var inactiveInput = new MemoryStream(Encoding.UTF8.GetBytes(gatewayEvent));
        using var inactiveResult = await function.FunctionHandler(inactiveInput);
        using var inactiveResponse = await JsonDocument.ParseAsync(inactiveResult);
        Assert.Equal(401, inactiveResponse.RootElement.GetProperty("statusCode").GetInt32());
        using var oldTokenRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/api/client/work-orders/{order.Id}");
        oldTokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var oldTokenResponse = await osClient.SendAsync(oldTokenRequest);
        Assert.Equal(HttpStatusCode.NotFound, oldTokenResponse.StatusCode);
    }

    private sealed class ForwardLookupHandler(HttpClient osClient) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal("/internal/customers/lookup", request.RequestUri!.AbsolutePath);
            using var forwarded = new HttpRequestMessage(request.Method,
                request.RequestUri.PathAndQuery)
            {
                Content = new ByteArrayContent(await request.Content!.ReadAsByteArrayAsync(
                    cancellationToken))
            };
            forwarded.Content.Headers.ContentType = request.Content.Headers.ContentType;
            forwarded.Headers.Add("X-Internal-Token",
                request.Headers.GetValues("X-Internal-Token"));
            return await osClient.SendAsync(forwarded, cancellationToken);
        }
    }
}
