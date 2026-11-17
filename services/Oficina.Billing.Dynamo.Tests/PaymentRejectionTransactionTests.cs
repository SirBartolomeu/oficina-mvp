using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using Oficina.Billing;
using Xunit;

namespace Oficina.Billing.Dynamo.Tests;

public sealed class PaymentRejectionTransactionTests
{
    [Theory]
    [InlineData("expired")]
    [InlineData("canceled")]
    public async Task TerminalUnpaidOrderPublishesOneRejection(string status)
    {
        var endpoint = Environment.GetEnvironmentVariable("BILLING_DYNAMO_ENDPOINT")
            ?? throw new InvalidOperationException("Defina BILLING_DYNAMO_ENDPOINT para o teste local.");
        using var dynamo = new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = endpoint });
        var table = "billing_reject_test_" + Guid.NewGuid().ToString("N");
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
        Assert.True(await repository.RecordPaymentRequestAsync(Guid.NewGuid(), payment,
            CancellationToken.None));
        Assert.True(await repository.CompletePaymentCreationAsync(payment.Id,
            new MercadoPagoQrOrder("ORDTEST123", "qr-test", "created"), CancellationToken.None));
        using var http = new HttpClient(new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                id = "ORDTEST123", external_reference = $"pay_{payment.Id:N}",
                status, currency = "BRL", total_amount = "125.00",
                transactions = new { payments = new[] { new { status } } }
            }), Encoding.UTF8, "application/json")
        })) { BaseAddress = new Uri("https://api.mercadopago.com/") };
        var reconciler = new PaymentReconciliationService(repository,
            new MercadoPagoQrClient(http, new MercadoPagoQrOptions("test-token", "test-pos")));

        Assert.True(await reconciler.ReconcileAsync("ORDTEST123", CancellationToken.None));
        Assert.False(await reconciler.ReconcileAsync("ORDTEST123", CancellationToken.None));
        Assert.Equal("Rejected",
            (await repository.GetPaymentAttemptAsync(payment.Id, CancellationToken.None))!.Status);
        var result = Assert.Single(await repository.GetPendingOutboxAsync(CancellationToken.None));
        using var body = JsonDocument.Parse(result.Body);
        Assert.Equal("PaymentRejected", body.RootElement.GetProperty("eventType").GetString());
        Assert.Equal(status, body.RootElement.GetProperty("payload")
            .GetProperty("reason").GetString());
    }

    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond());
    }
}
