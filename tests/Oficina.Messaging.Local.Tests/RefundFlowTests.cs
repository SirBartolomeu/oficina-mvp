using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.SQS;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Oficina.Billing;
using OficinaMvp.Api.Messaging;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Domain.Exceptions;
using OficinaMvp.Infrastructure.Messaging;
using OficinaMvp.Infrastructure.Persistence;
using Xunit;

namespace Oficina.Messaging.Local.Tests;

public sealed class RefundFlowTests
{
    [Fact]
    public async Task RefundRequestCrossesQueuesAndConfirmationCancelsOrder()
    {
        var sqsEndpoint = Environment.GetEnvironmentVariable("MESSAGING_SQS_ENDPOINT")
            ?? throw new InvalidOperationException("Defina MESSAGING_SQS_ENDPOINT para o teste local.");
        var dynamoEndpoint = Environment.GetEnvironmentVariable("BILLING_DYNAMO_ENDPOINT")
            ?? throw new InvalidOperationException("Defina BILLING_DYNAMO_ENDPOINT para o teste local.");
        using var sqs = new AmazonSQSClient(new BasicAWSCredentials("local", "local"),
            new AmazonSQSConfig { ServiceURL = sqsEndpoint });
        using var dynamo = new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = dynamoEndpoint });
        var billingQueue = (await sqs.CreateQueueAsync("billing-refund-" + Guid.NewGuid().ToString("N"))).QueueUrl;
        var osQueue = (await sqs.CreateQueueAsync("os-refund-" + Guid.NewGuid().ToString("N"))).QueueUrl;
        var table = "billing_refund_flow_" + Guid.NewGuid().ToString("N");
        await dynamo.CreateTableAsync(new CreateTableRequest
        {
            TableName = table, BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions = [new("pk", ScalarAttributeType.S), new("sk", ScalarAttributeType.S)],
            KeySchema = [new("pk", KeyType.HASH), new("sk", KeyType.RANGE)]
        });
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DynamoDb:Table"] = table, ["Messaging:WorkOrdersQueueUrl"] = billingQueue,
            ["Messaging:OsEventsQueueUrl"] = osQueue
        }).Build();
        var repository = new BillingRepository(dynamo, settings);
        var customer = new Customer("Cliente Teste", "52998224725", null, null);
        var vehicle = new Vehicle(customer.Id, "BRA2E19", "VW", "Gol", 2020);
        var order = new WorkOrder(customer.Id, vehicle.Id,
            [new WorkOrderServiceLine(Guid.NewGuid(), "Revisão", 1, 125m)],
            [], null, DateTime.UtcNow, "sp-centro");
        var budget = Budget.Create(order.Id, 1, order.BranchId,
            [new BudgetLine(Guid.NewGuid(), "Revisão", 1, 12500)]);
        await repository.CreateAsync(budget, CancellationToken.None);
        await repository.DecideAsync(order.Id, 1, true, CancellationToken.None);
        var paymentId = Guid.NewGuid();
        var payment = new PaymentRequest(paymentId, order.Id, budget.Id, 1,
            order.BranchId, 12500, DateTime.UtcNow);
        await repository.RecordPaymentRequestAsync(Guid.NewGuid(), payment, CancellationToken.None);
        await repository.CompletePaymentCreationAsync(paymentId,
            new MercadoPagoQrOrder("ORDTEST123", "qr-test", "created"), CancellationToken.None);
        await repository.ConfirmPaymentAsync(payment, "ORDTEST123", CancellationToken.None);
        foreach (var pending in await repository.GetPendingOutboxAsync(CancellationToken.None))
            await repository.MarkOutboxPublishedAsync(pending.EventId, CancellationToken.None);

        var reservationId = Guid.NewGuid();
        order.AcceptBudgetReady(budget.Id, 1, DateTime.UtcNow);
        order.ApproveBudgetForPayment(DateTime.UtcNow, reservationId);
        order.ConfirmStockReservation(DateTime.UtcNow, paymentId);
        order.ConfirmPayment(DateTime.UtcNow);
        order.StartExecutionAfterPayment(DateTime.UtcNow);
        order.RegisterExecutionRejection(DateTime.UtcNow, "Peça incompatível");
        var refundId = Guid.NewGuid();
        order.RequestRefundAfterReview(refundId, DateTime.UtcNow, "Serviço não realizado");
        var command = new IntegrationEnvelope<OficinaMvp.Application.Contracts.RefundPaymentRequestedPayload>(refundId,
            "RefundPaymentRequested", 1, order.Id, Guid.NewGuid(), order.Id,
            order.BranchId, DateTime.UtcNow,
            new OficinaMvp.Application.Contracts.RefundPaymentRequestedPayload(paymentId, refundId, "Serviço não realizado"));
        var connectionString = $"Data Source=refund-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        var options = new DbContextOptionsBuilder<WorkshopDbContext>()
            .UseSqlite(connectionString).Options;
        await using (var setup = new WorkshopDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.AddRange(customer, vehicle, order);
            setup.OutboxMessages.Add(new OutboxMessage(refundId, "RefundPaymentRequested",
                JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                DateTime.UtcNow));
            await setup.SaveChangesAsync();
            var sender = new SqsIntegrationMessageSender(sqs,
                new Dictionary<string, string> { ["RefundPaymentRequested"] = billingQueue });
            Assert.Equal(1, await new OutboxDispatcher(setup, sender,
                NullLogger<OutboxDispatcher>.Instance).DispatchBatchAsync(CancellationToken.None));
        }
        using var billingServices = new ServiceCollection()
            .AddScoped(_ => new BillingRepository(dynamo, settings))
            .AddScoped<RefundRequestHandler>()
            .BuildServiceProvider();
        using var osServices = new ServiceCollection()
            .AddScoped(_ => new WorkshopDbContext(options))
            .AddScoped<RefundConfirmedHandler>()
            .BuildServiceProvider();
        using var billingWorker = new BillingSqsWorker(sqs,
            billingServices.GetRequiredService<IServiceScopeFactory>(), settings,
            NullLogger<BillingSqsWorker>.Instance);
        using var outboxWorker = new BillingOutboxWorker(sqs,
            billingServices.GetRequiredService<IServiceScopeFactory>(), settings,
            NullLogger<BillingOutboxWorker>.Instance);
        using var osWorker = new OsEventsSqsWorker(sqs,
            osServices.GetRequiredService<IServiceScopeFactory>(), settings,
            NullLogger<OsEventsSqsWorker>.Instance);
        await billingWorker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () =>
            (await repository.GetPaymentAttemptAsync(paymentId, CancellationToken.None))?.Status == "RefundRequested");

        var postCount = 0;
        var refunded = false;
        using var http = new HttpClient(new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                postCount++;
                refunded = true;
                return Json(HttpStatusCode.Created, new { id = "ORDTEST123", status = "refunded" });
            }
            return Json(HttpStatusCode.OK, new
            {
                id = "ORDTEST123", external_reference = $"pay_{paymentId:N}",
                status = refunded ? "refunded" : "processed", currency = "BRL",
                total_amount = "125.00", transactions = new { payments = new[]
                {
                    new { status = refunded ? "refunded" : "processed", paid_amount = "125.00" }
                } }
            });
        })) { BaseAddress = new Uri("https://api.mercadopago.com/") };
        using var refundServices = new ServiceCollection()
            .AddScoped(_ => new RefundOrderProcessor(repository,
                new MercadoPagoQrClient(http, new MercadoPagoQrOptions("test-token", "test-pos")),
                NullLogger<RefundOrderProcessor>.Instance))
            .BuildServiceProvider();
        using var refundWorker = new RefundOrderWorker(
            refundServices.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RefundOrderWorker>.Instance);
        await refundWorker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () =>
            (await repository.GetPaymentAttemptAsync(paymentId, CancellationToken.None))?.Status == "Refunded");
        Assert.Equal(1, postCount);
        var confirmed = Assert.Single(await repository.GetPendingOutboxAsync(
            CancellationToken.None));
        var confirmedEnvelope = JsonSerializer.Deserialize<IntegrationEnvelope<OficinaMvp.Application.Contracts.RefundConfirmedPayload>>(
            confirmed.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var wrongRefund = confirmedEnvelope with
        {
            EventId = Guid.NewGuid(),
            Payload = confirmedEnvelope.Payload with { RefundRequestId = Guid.NewGuid() }
        };
        await using (var beforeConfirmation = new WorkshopDbContext(options))
        {
            await Assert.ThrowsAsync<DomainException>(() =>
                new RefundConfirmedHandler(beforeConfirmation).HandleAsync(
                    JsonSerializer.Serialize(wrongRefund,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    CancellationToken.None));
            Assert.Equal(WorkOrderProcessState.RefundRequested,
                (await beforeConfirmation.WorkOrders.SingleAsync(item => item.Id == order.Id))
                .ProcessState);
            Assert.Empty(await beforeConfirmation.InboxMessages.ToListAsync());
        }
        await outboxWorker.StartAsync(CancellationToken.None);
        await osWorker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () =>
        {
            await using var verify = new WorkshopDbContext(options);
            return (await verify.WorkOrders.SingleAsync(item => item.Id == order.Id)).ProcessState
                == WorkOrderProcessState.Canceled;
        });
        await using (var verify = new WorkshopDbContext(options))
        {
            var final = await verify.WorkOrders.SingleAsync(item => item.Id == order.Id);
            Assert.Equal(WorkOrderStatus.Canceled, final.Status);
            Assert.Single(await verify.InboxMessages.ToListAsync());
            Assert.False(await new RefundConfirmedHandler(verify).HandleAsync(
                confirmed.Body, CancellationToken.None));
            Assert.Single(await verify.InboxMessages.ToListAsync());
        }
        Assert.Empty(await repository.GetPendingRefundsAsync(CancellationToken.None));
        await billingWorker.StopAsync(CancellationToken.None);
        await refundWorker.StopAsync(CancellationToken.None);
        await outboxWorker.StopAsync(CancellationToken.None);
        await osWorker.StopAsync(CancellationToken.None);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!await condition())
        {
            if (timeout.IsCancellationRequested) throw new TimeoutException("Fluxo de estorno não concluiu.");
            await Task.Delay(100);
        }
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
