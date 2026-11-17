using System.Security.Cryptography;
using System.Text;
using Oficina.Billing;
using Xunit;

namespace Oficina.Billing.Tests;

public sealed class MercadoPagoWebhookSignatureTests
{
    [Fact]
    public void ValidatesExactOrderIdAndRejectsTampering()
    {
        const string orderId = "ORDTEST123";
        const string requestId = "request-123";
        const string timestamp = "1742505638683";
        const string secret = "test-secret";
        var manifest = $"id:{orderId};request-id:{requestId};ts:{timestamp};";
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(manifest));
        var signature = $"ts={timestamp},v1={Convert.ToHexStringLower(hash)}";

        Assert.True(MercadoPagoWebhookSignature.IsValid(signature, requestId, orderId, secret));
        Assert.False(MercadoPagoWebhookSignature.IsValid(signature, requestId, "ORDOTHER", secret));
        Assert.False(MercadoPagoWebhookSignature.IsValid(signature, requestId, orderId, "wrong"));
        Assert.False(MercadoPagoWebhookSignature.IsValid("ts=1,v1=bad", requestId, orderId, secret));
    }
}
