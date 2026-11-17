using System.Text.Json;
using System.Net;
using System.Text;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Oficina.Billing;
using Xunit;

namespace Oficina.Billing.Dynamo.Tests;

public sealed class BudgetDecisionTransactionTests
{
    [Theory]
    [InlineData(true, "BudgetApproved", BudgetStatus.Approved)]
    [InlineData(false, "BudgetRefused", BudgetStatus.Refused)]
    public async Task DecisionWritesStatusAndEventAtomicallyAndIgnoresRetry(
        bool approved, string eventType, BudgetStatus expectedStatus)
    {
        var endpoint = Environment.GetEnvironmentVariable("BILLING_DYNAMO_ENDPOINT")
            ?? throw new InvalidOperationException("Defina BILLING_DYNAMO_ENDPOINT para o teste local.");
        using var dynamo = new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = endpoint });
        var table = "billing_decision_test_" + Guid.NewGuid().ToString("N");
        await dynamo.CreateTableAsync(new CreateTableRequest
        {
            TableName = table,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions = [new("pk", ScalarAttributeType.S), new("sk", ScalarAttributeType.S)],
            KeySchema = [new("pk", KeyType.HASH), new("sk", KeyType.RANGE)]
        });
        var settings = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["DynamoDb:Table"] = table }).Build();
        var repository = new BillingRepository(dynamo, settings);
        var budget = Budget.Create(Guid.NewGuid(), 1, "sp-centro",
            [new BudgetLine(Guid.NewGuid(), "Revisão", 1, 12500)]);
        await repository.CreateAsync(budget, CancellationToken.None);

        var first = await repository.DecideAsync(budget.WorkOrderId, 1, approved, CancellationToken.None);
        var retry = await repository.DecideAsync(budget.WorkOrderId, 1, approved, CancellationToken.None);
        Assert.Equal(expectedStatus, first!.Status);
        Assert.Equal(expectedStatus, retry!.Status);
        Assert.Equal(expectedStatus,
            (await repository.GetAsync(budget.WorkOrderId, 1, CancellationToken.None))!.Status);
        var pending = Assert.Single(await repository.GetPendingOutboxAsync(CancellationToken.None));
        using var json = JsonDocument.Parse(pending.Body);
        Assert.Equal(eventType, json.RootElement.GetProperty("eventType").GetString());
        Assert.Equal(budget.Id, json.RootElement.GetProperty("payload").GetProperty("budgetId").GetGuid());
        Assert.Equal(pending.EventId,
            json.RootElement.GetProperty("payload").GetProperty("decisionId").GetGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.DecideAsync(budget.WorkOrderId, 1, !approved, CancellationToken.None));
        Assert.Single(await repository.GetPendingOutboxAsync(CancellationToken.None));

        var paymentAttemptId = Guid.NewGuid();
        var paymentEvent = new CreatePaymentRequestedEnvelope(Guid.NewGuid(),
            "CreatePaymentRequested", 1, budget.WorkOrderId, pending.EventId,
            budget.WorkOrderId, budget.BranchId, DateTime.UtcNow,
            new CreatePaymentRequestedPayload(paymentAttemptId, budget.Id, budget.Version,
                budget.TotalCents, "BRL"));
        var paymentBody = JsonSerializer.Serialize(paymentEvent,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var paymentHandler = new PaymentRequestHandler(repository);
        if (approved)
        {
            Assert.True(await paymentHandler.HandleAsync(paymentBody, CancellationToken.None));
            Assert.False(await paymentHandler.HandleAsync(paymentBody, CancellationToken.None));
            var saved = await repository.GetPaymentRequestAsync(paymentAttemptId, CancellationToken.None);
            Assert.Equal(budget.TotalCents, saved!.TotalCents);
            Assert.Equal(budget.Id, saved.BudgetId);
            var calls = 0;
            var queriedTotal = "125.01";
            using var http = new HttpClient(new StubHandler(request =>
            {
                if (request.Method == HttpMethod.Get)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(new
                        {
                            id = "ORDTEST", external_reference = $"pay_{paymentAttemptId:N}",
                            status = "processed", currency = "BRL", total_amount = queriedTotal,
                            transactions = new { payments = new[]
                            {
                                new { status = "processed", paid_amount = "125.00" }
                            } }
                        }), Encoding.UTF8, "application/json")
                    });
                calls++;
                Assert.Equal(paymentAttemptId.ToString("D"),
                    request.Headers.GetValues("X-Idempotency-Key").Single());
                return Task.FromResult(calls == 1
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : new HttpResponseMessage(HttpStatusCode.Created)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(new
                        {
                            id = "ORDTEST", status = "created",
                            external_reference = $"pay_{paymentAttemptId:N}",
                            total_amount = "125.00", type_response = new { qr_data = "qr-test" }
                        }), Encoding.UTF8, "application/json")
                    });
            })) { BaseAddress = new Uri("https://api.mercadopago.com/") };
            var processor = new PaymentOrderProcessor(repository,
                new MercadoPagoQrClient(http, new MercadoPagoQrOptions("test-token", "test-pos")),
                NullLogger<PaymentOrderProcessor>.Instance);
            Assert.Equal(0, await processor.ProcessBatchAsync(CancellationToken.None));
            Assert.Single(await repository.GetPendingPaymentsAsync(CancellationToken.None));
            Assert.Equal(1, await processor.ProcessBatchAsync(CancellationToken.None));
            Assert.Equal(0, await processor.ProcessBatchAsync(CancellationToken.None));
            Assert.Equal(2, calls);
            Assert.Empty(await repository.GetPendingPaymentsAsync(CancellationToken.None));
            var created = await repository.GetPaymentAttemptAsync(paymentAttemptId, CancellationToken.None);
            Assert.Equal("Created", created!.Status);
            Assert.Equal("ORDTEST", created.ProviderOrderId);
            Assert.Equal("qr-test", created.QrData);
            var reconciler = new PaymentReconciliationService(repository,
                new MercadoPagoQrClient(http, new MercadoPagoQrOptions("test-token", "test-pos")));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                reconciler.ReconcileAsync("ORDTEST", CancellationToken.None));
            Assert.Equal("Created",
                (await repository.GetPaymentAttemptAsync(paymentAttemptId, CancellationToken.None))!.Status);
            Assert.Single(await repository.GetPendingOutboxAsync(CancellationToken.None));
            queriedTotal = "125.00";
            Assert.True(await reconciler.ReconcileAsync("ORDTEST", CancellationToken.None));
            Assert.False(await reconciler.ReconcileAsync("ORDTEST", CancellationToken.None));
            Assert.Equal("Confirmed",
                (await repository.GetPaymentAttemptAsync(paymentAttemptId, CancellationToken.None))!.Status);
            var confirmed = (await repository.GetPendingOutboxAsync(CancellationToken.None))
                .Select(item => JsonDocument.Parse(item.Body))
                .Single(item => item.RootElement.GetProperty("eventType").GetString() == "PaymentConfirmed");
            Assert.Equal(paymentAttemptId,
                confirmed.RootElement.GetProperty("payload").GetProperty("paymentAttemptId").GetGuid());
            confirmed.Dispose();
            var oldAttemptId = Guid.NewGuid();
            var oldEvent = paymentEvent with
            {
                EventId = Guid.NewGuid(), OccurredAtUtc = DateTime.UtcNow.AddHours(-24),
                Payload = paymentEvent.Payload with { PaymentAttemptId = oldAttemptId }
            };
            Assert.True(await paymentHandler.HandleAsync(JsonSerializer.Serialize(oldEvent,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)), CancellationToken.None));
            Assert.Equal(0, await processor.ProcessBatchAsync(CancellationToken.None));
            Assert.Equal("ManualReview",
                (await repository.GetPaymentAttemptAsync(oldAttemptId, CancellationToken.None))!.Status);
            Assert.Equal(2, calls);
            Assert.Empty(await repository.GetPendingPaymentsAsync(CancellationToken.None));
            var tampered = paymentEvent with
            {
                EventId = Guid.NewGuid(),
                Payload = paymentEvent.Payload with { TotalCents = 1 }
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() => paymentHandler.HandleAsync(
                JsonSerializer.Serialize(tampered, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                CancellationToken.None));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                paymentHandler.HandleAsync(paymentBody, CancellationToken.None));
            Assert.Null(await repository.GetPaymentRequestAsync(paymentAttemptId, CancellationToken.None));
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }
}
