using System.Net;
using System.Text;
using System.Text.Json;
using Oficina.Billing;
using Xunit;

namespace Oficina.Billing.Tests;

public sealed class MercadoPagoQrClientTests
{
    [Fact]
    public async Task CreateUsesStableIdempotencyAndExactCents()
    {
        var payment = new PaymentRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            1, "sp-centro", 12545, DateTime.UtcNow);
        var reference = $"pay_{payment.Id:N}";
        var handler = new StubHandler(async request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            Assert.Equal(payment.Id.ToString("D"),
                request.Headers.GetValues("X-Idempotency-Key").Single());
            Assert.Equal("/v1/orders", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("125.45", body.RootElement.GetProperty("total_amount").GetString());
            Assert.Equal("125.45", body.RootElement.GetProperty("transactions")
                .GetProperty("payments")[0].GetProperty("amount").GetString());
            Assert.Equal("dynamic", body.RootElement.GetProperty("config")
                .GetProperty("qr").GetProperty("mode").GetString());
            Assert.Equal("test-pos", body.RootElement.GetProperty("config")
                .GetProperty("qr").GetProperty("external_pos_id").GetString());
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    id = "ORDTEST", status = "created", external_reference = reference,
                    total_amount = "125.45", type_response = new { qr_data = "qr-test-data" }
                }), Encoding.UTF8, "application/json")
            };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.mercadopago.com/") };
        var client = new MercadoPagoQrClient(http, new MercadoPagoQrOptions("test-token", "test-pos"));

        var order = await client.CreateAsync(payment, CancellationToken.None);

        Assert.Equal("ORDTEST", order.Id);
        Assert.Equal("qr-test-data", order.QrData);
    }

    [Fact]
    public async Task GetReadsProviderStatusAndPaidAmount()
    {
        using var http = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/v1/orders/ORDTEST123", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    id = "ORDTEST123", external_reference = "pay_123", status = "processed",
                    currency = "BRL", total_amount = "125.45",
                    transactions = new { payments = new[]
                    {
                        new { status = "processed", paid_amount = "125.45" }
                    } }
                }), Encoding.UTF8, "application/json")
            });
        })) { BaseAddress = new Uri("https://api.mercadopago.com/") };
        var client = new MercadoPagoQrClient(http, new MercadoPagoQrOptions("test-token", "test-pos"));

        var order = await client.GetAsync("ORDTEST123", CancellationToken.None);

        Assert.Equal(12545, order.TotalCents);
        Assert.Equal(12545, order.PaidCents);
        Assert.Equal("processed", order.Status);
        Assert.Equal("processed", order.PaymentStatus);
    }

    [Fact]
    public async Task RefundUsesOrderEndpointAndStableIdempotencyWithoutAmountBody()
    {
        var requestId = Guid.NewGuid();
        using var http = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/orders/ORDTEST123/refund", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            Assert.Equal(requestId.ToString("D"),
                request.Headers.GetValues("X-Idempotency-Key").Single());
            Assert.Null(request.Content);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{\"id\":\"ORDTEST123\",\"status\":\"refunded\"}",
                    Encoding.UTF8, "application/json")
            });
        })) { BaseAddress = new Uri("https://api.mercadopago.com/") };
        var client = new MercadoPagoQrClient(http,
            new MercadoPagoQrOptions("test-token", "test-pos"));

        var submission = await client.RequestRefundAsync("ORDTEST123", requestId,
            CancellationToken.None);

        Assert.Equal("ORDTEST123", submission.OrderId);
        Assert.Equal("refunded", submission.Status);
    }

    [Fact]
    public async Task RefundRejectsResponseForAnotherOrder()
    {
        using var http = new HttpClient(new StubHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{\"id\":\"ORDOTHER\",\"status\":\"refunded\"}",
                    Encoding.UTF8, "application/json")
            }))) { BaseAddress = new Uri("https://api.mercadopago.com/") };
        var client = new MercadoPagoQrClient(http,
            new MercadoPagoQrOptions("test-token", "test-pos"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RequestRefundAsync(
            "ORDTEST123", Guid.NewGuid(), CancellationToken.None));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }
}
