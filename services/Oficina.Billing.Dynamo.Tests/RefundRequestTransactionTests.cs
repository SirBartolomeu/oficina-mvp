using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using Oficina.Billing;
using Xunit;

namespace Oficina.Billing.Dynamo.Tests;

public sealed class RefundRequestTransactionTests
{
    [Fact]
    public async Task ConfirmedPaymentAcceptsOneRefundRequestAndKeepsItPending()
    {
        var endpoint = Environment.GetEnvironmentVariable("BILLING_DYNAMO_ENDPOINT")
            ?? throw new InvalidOperationException("Defina BILLING_DYNAMO_ENDPOINT para o teste local.");
        using var dynamo = new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = endpoint });
        var table = "billing_refund_test_" + Guid.NewGuid().ToString("N");
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
        var payment = new PaymentRequest(Guid.NewGuid(), budget.WorkOrderId, budget.Id,
            1, budget.BranchId, budget.TotalCents, DateTime.UtcNow);
        await repository.RecordPaymentRequestAsync(Guid.NewGuid(), payment, CancellationToken.None);
        await repository.CompletePaymentCreationAsync(payment.Id,
            new MercadoPagoQrOrder("ORDTEST123", "qr-test", "created"), CancellationToken.None);
        await repository.ConfirmPaymentAsync(payment, "ORDTEST123", CancellationToken.None);
        var requestId = Guid.NewGuid();
        var request = new RefundPaymentRequestedEnvelope(requestId,
            "RefundPaymentRequested", 1, budget.WorkOrderId, Guid.NewGuid(),
            budget.WorkOrderId, budget.BranchId, DateTime.UtcNow,
            new RefundPaymentRequestedPayload(payment.Id, requestId, "Serviço não realizado"));
        var body = JsonSerializer.Serialize(request,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var handler = new RefundRequestHandler(repository);

        Assert.True(await handler.HandleAsync(body, CancellationToken.None));
        Assert.False(await handler.HandleAsync(body, CancellationToken.None));
        var attempt = await repository.GetPaymentAttemptAsync(payment.Id, CancellationToken.None);
        Assert.Equal("RefundRequested", attempt!.Status);
        Assert.Equal(requestId, attempt.RefundRequestId);
        Assert.Equal("Serviço não realizado", attempt.RefundReason);
        Assert.Equal(payment.Id, Assert.Single(await repository.GetPendingRefundsAsync(
            CancellationToken.None)));
        var anotherId = Guid.NewGuid();
        var conflicting = request with { EventId = anotherId,
            Payload = request.Payload with { RefundRequestId = anotherId } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            JsonSerializer.Serialize(conflicting,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)), CancellationToken.None));
        var changedReason = request with { Payload = request.Payload with
        {
            Reason = "Outro motivo"
        } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            JsonSerializer.Serialize(changedReason,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)), CancellationToken.None));
    }
}
