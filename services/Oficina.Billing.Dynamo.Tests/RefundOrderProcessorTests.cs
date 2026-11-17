using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Oficina.Billing;
using Xunit;

namespace Oficina.Billing.Dynamo.Tests;

public sealed class RefundOrderProcessorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefundIsConfirmedOnlyByGetAndDoesNotRepeatAmbiguousPost(bool postFails)
    {
        var endpoint = Environment.GetEnvironmentVariable("BILLING_DYNAMO_ENDPOINT")
            ?? throw new InvalidOperationException("Defina BILLING_DYNAMO_ENDPOINT para o teste local.");
        using var dynamo = new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = endpoint });
        var table = "billing_refund_worker_" + Guid.NewGuid().ToString("N");
        await dynamo.CreateTableAsync(new CreateTableRequest
        {
            TableName = table, BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions = [new("pk", ScalarAttributeType.S), new("sk", ScalarAttributeType.S)],
            KeySchema = [new("pk", KeyType.HASH), new("sk", KeyType.RANGE)]
        });
        var settings = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["DynamoDb:Table"] = table }).Build();
        var repository = new BillingRepository(dynamo, settings);
        var budget = Budget.Create(Guid.NewGuid(), 1, "sp-centro",
            [new BudgetLine(Guid.NewGuid(), "Revisão", 1, 12500)]);
        await repository.CreateAsync(budget, CancellationToken.None);
        await repository.DecideAsync(budget.WorkOrderId, 1, true, CancellationToken.None);
        foreach (var pending in await repository.GetPendingOutboxAsync(CancellationToken.None))
            await repository.MarkOutboxPublishedAsync(pending.EventId, CancellationToken.None);
        var payment = new PaymentRequest(Guid.NewGuid(), budget.WorkOrderId, budget.Id,
            1, budget.BranchId, budget.TotalCents, DateTime.UtcNow);
        await repository.RecordPaymentRequestAsync(Guid.NewGuid(), payment, CancellationToken.None);
        await repository.CompletePaymentCreationAsync(payment.Id,
            new MercadoPagoQrOrder("ORDTEST123", "qr-test", "created"), CancellationToken.None);
        await repository.ConfirmPaymentAsync(payment, "ORDTEST123", CancellationToken.None);
        foreach (var pending in await repository.GetPendingOutboxAsync(CancellationToken.None))
            await repository.MarkOutboxPublishedAsync(pending.EventId, CancellationToken.None);
        var refundId = Guid.NewGuid();
        await repository.RecordRefundRequestAsync(refundId, payment.Id,
            "Serviço não realizado", CancellationToken.None);

        var postCount = 0;
        var refunded = false;
        using var http = new HttpClient(new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                postCount++;
                Assert.Equal(refundId.ToString("D"),
                    request.Headers.GetValues("X-Idempotency-Key").Single());
                if (postFails) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                refunded = true;
                return Json(HttpStatusCode.Created, new { id = "ORDTEST123", status = "refunded" });
            }
            return Json(HttpStatusCode.OK, new
            {
                id = "ORDTEST123", external_reference = $"pay_{payment.Id:N}",
                status = refunded ? "refunded" : "processed", currency = "BRL",
                total_amount = "125.00", transactions = new { payments = new[]
                {
                    new { status = refunded ? "refunded" : "processed", paid_amount = "125.00" }
                } }
            });
        })) { BaseAddress = new Uri("https://api.mercadopago.com/") };
        var client = new MercadoPagoQrClient(http,
            new MercadoPagoQrOptions("test-token", "test-pos"));
        var processor = new RefundOrderProcessor(repository, client,
            NullLogger<RefundOrderProcessor>.Instance);

        Assert.Equal(postFails ? 0 : 1, await processor.ProcessBatchAsync(CancellationToken.None));
        Assert.Equal(1, postCount);
        Assert.Equal(postFails ? "RefundSubmitting" : "Refunded",
            (await repository.GetPaymentAttemptAsync(payment.Id, CancellationToken.None))!.Status);
        if (postFails)
        {
            Assert.Equal(0, await processor.ProcessBatchAsync(CancellationToken.None));
            Assert.Equal(1, postCount);
            refunded = true; // a consulta posterior comprova o resultado ambíguo
            Assert.True(await new PaymentReconciliationService(repository, client)
                .ReconcileAsync("ORDTEST123", CancellationToken.None));
        }
        Assert.Equal("Refunded",
            (await repository.GetPaymentAttemptAsync(payment.Id, CancellationToken.None))!.Status);
        Assert.Empty(await repository.GetPendingRefundsAsync(CancellationToken.None));
        Assert.Equal(0, await processor.ProcessBatchAsync(CancellationToken.None));
        Assert.Equal(1, postCount);
        var result = Assert.Single(await repository.GetPendingOutboxAsync(CancellationToken.None));
        using var body = JsonDocument.Parse(result.Body);
        Assert.Equal("RefundConfirmed", body.RootElement.GetProperty("eventType").GetString());
        Assert.Equal(refundId, body.RootElement.GetProperty("payload")
            .GetProperty("refundRequestId").GetGuid());
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object value) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
