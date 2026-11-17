using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Oficina.Billing;
using Xunit;

namespace Oficina.Billing.Dynamo.Tests;

[Collection("Billing HTTP environment")]
public sealed class CheckoutProSagaTests
{
    [Theory]
    [InlineData("processed", "Confirmed", "PaymentConfirmed", "accredited")]
    [InlineData("failed", "Rejected", "PaymentRejected", "rejected_by_issuer")]
    [InlineData("canceled", "Rejected", "PaymentRejected", "canceled")]
    [InlineData("processed", "ManualReview", null, "partially_refunded")]
    public async Task SignedTestWebhookReconcilesPersistedCheckoutExactlyOnce(
        string providerStatus, string expectedStatus, string? expectedEvent, string statusDetail)
    {
        var endpoint = Environment.GetEnvironmentVariable("BILLING_DYNAMO_ENDPOINT")!;
        using var dynamo = new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = endpoint });
        var table = "checkout_test_" + Guid.NewGuid().ToString("N");
        await dynamo.CreateTableAsync(new CreateTableRequest
        {
            TableName = table, BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions = [new("pk", ScalarAttributeType.S), new("sk", ScalarAttributeType.S)],
            KeySchema = [new("pk", KeyType.HASH), new("sk", KeyType.RANGE)]
        });
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["DynamoDb:Table"] = table }).Build();
        var repository = new BillingRepository(dynamo, settings);
        var budget = Budget.Create(Guid.NewGuid(), 1, "sp", [new BudgetLine(Guid.NewGuid(), "Revisão", 1, 12545)]);
        await repository.CreateAsync(budget, default);
        await repository.DecideAsync(budget.WorkOrderId, 1, true, default);
        foreach (var message in await repository.GetPendingOutboxAsync(default))
            await repository.MarkOutboxPublishedAsync(message.EventId, default);
        var payment = new PaymentRequest(Guid.NewGuid(), budget.WorkOrderId, budget.Id, 1, "sp", 12545, DateTime.UtcNow);
        var command = new CreatePaymentRequestedEnvelope(Guid.NewGuid(), "CreatePaymentRequested", 1,
            budget.WorkOrderId, Guid.NewGuid(), budget.WorkOrderId, "sp", DateTime.UtcNow,
            new(payment.Id, budget.Id, 1, payment.TotalCents, "BRL"));
        var handler = new PaymentRequestHandler(repository);
        var body = JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(await handler.HandleAsync(body, default));
        Assert.False(await handler.HandleAsync(body, default));
        var orderPosts = 0;
        var failLookup = false;
        using var providerHttp = new HttpClient(new Stub(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/users/me")
                return Json(new { site_id = "MLB", tags = new[] { "test_user" } });
            if (request.Method == HttpMethod.Post)
            {
                orderPosts++;
                using var data = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.Equal("125.45", data.RootElement.GetProperty("total_amount").GetString());
                return Json(new { id = "ORDTST123", status = "created", currency = "BRL",
                    total_amount = "125.45", external_reference = $"pay_{payment.Id:N}",
                    checkout_url = "https://www.mercadopago.com.br/checkout/v1/redirect?order_id=ORDTST123" });
            }
            if (failLookup) return new(HttpStatusCode.ServiceUnavailable);
            return Json(new { id = "ORDTST123", status = providerStatus, status_detail = statusDetail, currency = "BRL", live_mode = false,
                total_amount = "125.45", external_reference = $"pay_{payment.Id:N}",
                transactions = new { payments = new[] { new { status = providerStatus,
                    paid_amount = providerStatus == "processed" ? "125.45" : "0.00" } } } });
        })) { BaseAddress = new("https://api.mercadopago.com/") };
        var provider = new MercadoPagoQrClient(providerHttp, new("APP_USR-test", "") { CheckoutPro = true, TestMode = true });
        var processor = new PaymentOrderProcessor(repository, provider, NullLogger<PaymentOrderProcessor>.Instance);
        Assert.Equal(1, await processor.ProcessBatchAsync(default));
        Assert.Equal(0, await processor.ProcessBatchAsync(default));
        Assert.Equal(1, orderPosts);
        var saved = await repository.GetPaymentAttemptAsync(payment.Id, default);
        Assert.NotNull(saved!.CheckoutUrl);
        Assert.Null(saved.QrData);
        Assert.Equal(12545, saved.TotalCents);

        const string internalToken = "checkout-local-internal-token-123456789";
        var values = new Dictionary<string, string?>
        {
            ["InternalToken"] = internalToken, ["DynamoDb__Table"] = table,
            ["DynamoDb__ServiceUrl"] = endpoint, ["MercadoPago__AccessToken"] = "APP_USR-test",
            ["MercadoPago__Mode"] = "CheckoutPro", ["MercadoPago__TestMode"] = "true",
            ["MercadoPago__WebhookSecret"] = "webhook-test-secret", ["MercadoPago__ExternalPosId"] = null
        };
        var previous = values.ToDictionary(pair => pair.Key, pair => Environment.GetEnvironmentVariable(pair.Key));
        foreach (var pair in values) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureTestServices(services =>
                {
                    services.AddScoped(_ => provider);
                    // Processar explicitamente permite testar falhas entre recebimento e reconciliação.
                    foreach (var service in services.Where(s => s.ServiceType == typeof(IHostedService)).ToArray())
                        services.Remove(service);
                });
            });
            using var client = factory.CreateClient();
            var url = $"/internal/payments/{payment.Id}";
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
            client.DefaultRequestHeaders.Add("X-Internal-Token", internalToken);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/dev/checkout")).StatusCode);
            const string webhookUrl = "/webhooks/mercado-pago/orders?data.id=ORDTST123";
            var payload = JsonSerializer.Serialize(new { type = "order", live_mode = false, data = new { id = "ORDTST123" } });
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(webhookUrl,
                new StringContent(payload, Encoding.UTF8, "application/json"))).StatusCode);
            const string ts = "1742505638683";
            var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("webhook-test-secret"),
                Encoding.UTF8.GetBytes($"id:ORDTST123;request-id:test-request;ts:{ts};")));
            client.DefaultRequestHeaders.Add("x-signature", $"ts={ts},v1={signature}");
            client.DefaultRequestHeaders.Add("x-request-id", "test-request");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(webhookUrl,
                new StringContent(payload.Replace("false", "true"), Encoding.UTF8, "application/json"))).StatusCode);
            for (var i = 0; i < 2; i++)
                Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(webhookUrl,
                    new StringContent(payload, Encoding.UTF8, "application/json"))).StatusCode);
            Assert.Equal("Created", (await repository.GetPaymentAttemptAsync(payment.Id, default))!.Status);
            var reconciliation = new PaymentReconciliationService(repository, provider);
            failLookup = true;
            Assert.Equal(0, await new OrderNotificationQueue(dynamo, settings).ProcessAsync(reconciliation, NullLogger.Instance, default));
            failLookup = false;
            Assert.Equal(2, await new OrderNotificationQueue(dynamo, settings).ProcessAsync(reconciliation, NullLogger.Instance, default));
            Assert.Equal(expectedStatus, (await repository.GetPaymentAttemptAsync(payment.Id, default))!.Status);
            var events = await repository.GetPendingOutboxAsync(default);
            if (expectedEvent is null) Assert.Empty(events);
            else
            {
                using var eventJson = JsonDocument.Parse(Assert.Single(events).Body);
                Assert.Equal(expectedEvent, eventJson.RootElement.GetProperty("eventType").GetString());
            }
        }
        finally
        {
            foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            await dynamo.DeleteTableAsync(table);
        }
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private sealed class Stub(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }
}
