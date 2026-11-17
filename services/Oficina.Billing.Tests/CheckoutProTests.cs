using System.Net;
using System.Text;
using System.Text.Json;
using Oficina.Billing;
using Xunit;

namespace Oficina.Billing.Tests;

public sealed class CheckoutProTests
{
    private static PaymentRequest Payment() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        1, "sp", 12545, DateTime.UtcNow);
    private static MercadoPagoQrOptions Options(bool testMode = true) => new("APP_USR-test", "")
    { CheckoutPro = true, TestMode = testMode };
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    [Fact]
    public async Task CreatesOnlineManualOrderAfterVerifyingTestIdentity()
    {
        var payment = Payment();
        var calls = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            calls++;
            if (calls == 1)
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/users/me", request.RequestUri!.AbsolutePath);
                return Json(new { site_id = "MLB", tags = new[] { "test_user" } });
            }
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(payment.Id.ToString("D"), request.Headers.GetValues("X-Idempotency-Key").Single());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = body.RootElement;
            Assert.Equal("online", root.GetProperty("type").GetString());
            Assert.Equal("manual", root.GetProperty("processing_mode").GetString());
            Assert.Equal("125.45", root.GetProperty("items")[0].GetProperty("unit_price").GetString());
            Assert.False(root.TryGetProperty("transactions", out _));
            Assert.False(root.GetProperty("config").GetProperty("online").TryGetProperty("auto_return", out _));
            return Json(new { id = "ORDTST123", status = "created", currency = "BRL",
                external_reference = $"pay_{payment.Id:N}", total_amount = "125.45",
                checkout_url = "https://www.mercadopago.com.br/checkout/v1/redirect?order_id=ORDTST123" });
        })) { BaseAddress = new("https://api.mercadopago.com/") };
        var order = await new MercadoPagoQrClient(http, Options()).CreateAsync(payment, default);
        Assert.Equal(2, calls);
        Assert.NotNull(order.CheckoutUrl);
        Assert.Null(order.QrData);
    }

    [Theory]
    [InlineData(false, true, "MLB", 0)]
    [InlineData(true, false, "MLB", 1)]
    [InlineData(true, true, "MLA", 1)]
    public async Task BlocksPostUnlessTestIdentityIsVerified(bool testMode, bool testUser, string site, int expectedCalls)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(Json(new { site_id = site, tags = testUser ? new[] { "test_user" } : new[] { "normal" } }));
        })) { BaseAddress = new("https://api.mercadopago.com/") };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new MercadoPagoQrClient(http, Options(testMode)).CreateAsync(Payment(), default));
        Assert.Equal(expectedCalls, calls);
    }

    [Theory]
    [InlineData("created")]
    [InlineData("canceled")]
    public async Task ReadsOrdersWithoutTransactions(string status)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Json(new
        { id = "ORDTST123", status, currency = "BRL", external_reference = "pay_123", total_amount = "125.45" }))))
        { BaseAddress = new("https://api.mercadopago.com/") };
        var order = await new MercadoPagoQrClient(http, Options()).GetAsync("ORDTST123", default);
        Assert.Equal(0, order.PaidCents);
        Assert.Equal(status, order.Status);
    }

    [Fact]
    public async Task NeverReadsOrRefundsProductionOrders()
    {
        using var http = new HttpClient(new Handler(_ => throw new Exception("No network expected")))
        { BaseAddress = new("https://api.mercadopago.com/") };
        var client = new MercadoPagoQrClient(http, Options());
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("ORDPROD", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RequestRefundAsync("ORDPROD", Guid.NewGuid(), default));
    }

    [Theory]
    [InlineData("https://www.mercadopago.com.br.evil.test/checkout/v1/redirect?order_id=ORDTST123")]
    [InlineData("https://www.mercadopago.com.br/checkout/v1/redirect?order_id=ORDOTHER")]
    [InlineData("http://www.mercadopago.com.br/checkout/v1/redirect?order_id=ORDTST123")]
    public void RejectsUnsafeCheckoutLinks(string url) => Assert.False(MercadoPagoQrClient.IsCheckoutUrl(url, "ORDTST123"));

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }
}
