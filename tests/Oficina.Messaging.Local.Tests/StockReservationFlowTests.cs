extern alias execution;

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
using ExecutionCommandsWorker = execution::Oficina.Execution.ExecutionCommandsWorker;
using ExecutionOutboxWorker = execution::Oficina.Execution.ExecutionOutboxWorker;
using ExecutionService = execution::Oficina.Execution.ExecutionService;
using ReserveStockRequestedHandler = execution::Oficina.Execution.ReserveStockRequestedHandler;
using ReleaseStockRequestedHandler = execution::Oficina.Execution.ReleaseStockRequestedHandler;
using ConsumeStockRequestedHandler = execution::Oficina.Execution.ConsumeStockRequestedHandler;
using ExecutionRequestedHandler = execution::Oficina.Execution.ExecutionRequestedHandler;
using ExecutionDbContext = execution::Oficina.Execution.Persistence.ExecutionDbContext;
using ReservationStatus = execution::Oficina.Execution.Persistence.ReservationStatus;
using ExecutionPartSupply = execution::OficinaMvp.Domain.Entities.PartSupply;
using OficinaMvp.Api.Messaging;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Infrastructure.Messaging;
using OficinaMvp.Infrastructure.Persistence;
using Billing = Oficina.Billing;
using Xunit;

namespace Oficina.Messaging.Local.Tests;

public sealed class StockReservationFlowTests
{
    [Theory]
    [InlineData(2, 3, WorkOrderProcessState.AwaitingPayment, false, "expired")]
    [InlineData(2, 3, WorkOrderProcessState.AwaitingPayment, false, "canceled")]
    [InlineData(2, 3, WorkOrderProcessState.AwaitingPayment, false, "failed")]
    [InlineData(4, 1, WorkOrderProcessState.Canceled, false, "expired")]
    [InlineData(2, 3, WorkOrderProcessState.AwaitingPayment, true, "expired")]
    public async Task ReservationResultUpdatesOrderWithoutOverselling(
        int requested, int expectedStock, WorkOrderProcessState expectedState, bool paid,
        string rejectionReason)
    {
        var endpoint = Environment.GetEnvironmentVariable("MESSAGING_SQS_ENDPOINT")
            ?? throw new InvalidOperationException("Defina MESSAGING_SQS_ENDPOINT para o teste local.");
        using var sqs = new AmazonSQSClient(new BasicAWSCredentials("local", "local"),
            new AmazonSQSConfig { ServiceURL = endpoint });
        var commandsQueue = (await sqs.CreateQueueAsync("execution-test-" + Guid.NewGuid().ToString("N"))).QueueUrl;
        var osQueue = (await sqs.CreateQueueAsync("os-reserve-test-" + Guid.NewGuid().ToString("N"))).QueueUrl;
        var osConnectionString = $"Data Source=os-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var executionConnectionString = $"Data Source=execution-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var osAnchor = new SqliteConnection(osConnectionString);
        await using var executionAnchor = new SqliteConnection(executionConnectionString);
        await osAnchor.OpenAsync();
        await executionAnchor.OpenAsync();
        var osOptions = new DbContextOptionsBuilder<WorkshopDbContext>().UseSqlite(osConnectionString).Options;
        var executionOptions = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseSqlite(executionConnectionString).Options;
        var part = new ExecutionPartSupply("Filtro", 10m, expectedStock);
        await using (var executionSetup = new ExecutionDbContext(executionOptions))
        {
            await executionSetup.Database.EnsureCreatedAsync();
            executionSetup.PartSupplies.Add(part);
            await executionSetup.SaveChangesAsync();
        }
        var customer = new Customer("Cliente Teste", "52998224725", null, null);
        var vehicle = new Vehicle(customer.Id, "BRA2E19", "VW", "Gol", 2020);
        var order = new WorkOrder(customer.Id, vehicle.Id, [],
            [new WorkOrderPartLine(part.Id, part.Name, requested, part.UnitPrice)],
            null, DateTime.UtcNow, "sp-centro");
        order.AcceptBudgetReady(Guid.NewGuid(), 1, DateTime.UtcNow);
        var reservationId = Guid.NewGuid();
        order.ApproveBudgetForPayment(DateTime.UtcNow, reservationId);
        var requestEvent = new IntegrationEnvelope<ReserveStockRequestedPayload>(
            Guid.NewGuid(), "ReserveStockRequested", 1, order.Id, Guid.NewGuid(),
            order.Id, order.BranchId, DateTime.UtcNow,
            new ReserveStockRequestedPayload(reservationId, 1,
                [new StockItem(part.Id, requested)], DateTime.UtcNow.AddMinutes(15)));
        await using (var osSetup = new WorkshopDbContext(osOptions))
        {
            await osSetup.Database.EnsureCreatedAsync();
            osSetup.AddRange(customer, vehicle, order);
            osSetup.OutboxMessages.Add(new OutboxMessage(requestEvent.EventId,
                requestEvent.EventType, JsonSerializer.Serialize(requestEvent,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)), DateTime.UtcNow));
            await osSetup.SaveChangesAsync();
            var sender = new SqsIntegrationMessageSender(sqs,
                new Dictionary<string, string> { ["ReserveStockRequested"] = commandsQueue });
            await new OutboxDispatcher(osSetup, sender, NullLogger<OutboxDispatcher>.Instance)
                .DispatchBatchAsync(CancellationToken.None);
        }

        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:CommandsQueueUrl"] = commandsQueue,
            ["Messaging:OsEventsQueueUrl"] = osQueue
        }).Build();
        using var executionServices = new ServiceCollection()
            .AddScoped(_ => new ExecutionDbContext(executionOptions))
            .AddScoped<ExecutionService>()
            .AddScoped<ReserveStockRequestedHandler>()
            .AddScoped<ReleaseStockRequestedHandler>()
            .AddScoped<ConsumeStockRequestedHandler>()
            .AddScoped<ExecutionRequestedHandler>()
            .BuildServiceProvider();
        using var osServices = new ServiceCollection()
            .AddScoped(_ => new WorkshopDbContext(osOptions))
            .AddScoped<BudgetReadyHandler>()
            .AddScoped<StockReservationResultHandler>()
            .AddScoped<PaymentRejectedHandler>()
            .AddScoped<PaymentConfirmedHandler>()
            .AddScoped<StockReleasedHandler>()
            .AddScoped<StockConsumedHandler>()
            .AddScoped<ExecutionStartedHandler>()
            .BuildServiceProvider();
        using var commandsWorker = new ExecutionCommandsWorker(sqs,
            executionServices.GetRequiredService<IServiceScopeFactory>(), settings,
            NullLogger<ExecutionCommandsWorker>.Instance);
        using var executionOutbox = new ExecutionOutboxWorker(sqs,
            executionServices.GetRequiredService<IServiceScopeFactory>(), settings,
            NullLogger<ExecutionOutboxWorker>.Instance);
        using var osWorker = new OsEventsSqsWorker(sqs,
            osServices.GetRequiredService<IServiceScopeFactory>(), settings,
            NullLogger<OsEventsSqsWorker>.Instance);
        await commandsWorker.StartAsync(CancellationToken.None);
        await executionOutbox.StartAsync(CancellationToken.None);
        await osWorker.StartAsync(CancellationToken.None);
        WorkOrderProcessState state = WorkOrderProcessState.AwaitingStockReservation;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            while (state == WorkOrderProcessState.AwaitingStockReservation && !timeout.IsCancellationRequested)
            {
                await using var verify = new WorkshopDbContext(osOptions);
                state = (await verify.WorkOrders.SingleAsync(item => item.Id == order.Id)).ProcessState;
                if (state == WorkOrderProcessState.AwaitingStockReservation)
                    await Task.Delay(100, timeout.Token);
            }
        }
        Assert.Equal(expectedState, state);
        if (expectedState == WorkOrderProcessState.AwaitingPayment)
        {
            await using (var reservedStock = new ExecutionDbContext(executionOptions))
                Assert.Equal(expectedStock - requested,
                    (await reservedStock.PartSupplies.SingleAsync(item => item.Id == part.Id)).StockQuantity);
            await using var beforeRejection = new WorkshopDbContext(osOptions);
            var pendingOrder = await beforeRejection.WorkOrders.AsNoTracking()
                .SingleAsync(item => item.Id == order.Id);
            if (paid)
            {
                var confirmed = new IntegrationEnvelope<PaymentConfirmedPayload>(Guid.NewGuid(),
                    "PaymentConfirmed", 1, order.Id, Guid.NewGuid(), order.Id, order.BranchId,
                    DateTime.UtcNow, new PaymentConfirmedPayload(
                        pendingOrder.PaymentAttemptId!.Value, "ORDTEST123", requested * 1000L,
                        "BRL"));
                await sqs.SendMessageAsync(osQueue, JsonSerializer.Serialize(confirmed,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                {
                    while (state == WorkOrderProcessState.AwaitingPayment && !timeout.IsCancellationRequested)
                    {
                        await using var verify = new WorkshopDbContext(osOptions);
                        state = (await verify.WorkOrders.SingleAsync(item => item.Id == order.Id)).ProcessState;
                        if (state == WorkOrderProcessState.AwaitingPayment) await Task.Delay(100);
                    }
                }
                Assert.Equal(WorkOrderProcessState.ReadyForExecution, state);
                await using (var dispatch = new WorkshopDbContext(osOptions))
                {
                    var paymentCommand = await dispatch.OutboxMessages
                        .SingleAsync(item => item.EventType == "CreatePaymentRequested");
                    paymentCommand.MarkPublished(DateTime.UtcNow);
                    await dispatch.SaveChangesAsync();
                    var sender = new SqsIntegrationMessageSender(sqs,
                        new Dictionary<string, string> { ["ConsumeStockRequested"] = commandsQueue });
                    Assert.Equal(1, await new OutboxDispatcher(dispatch, sender,
                        NullLogger<OutboxDispatcher>.Instance).DispatchBatchAsync(CancellationToken.None));
                }
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                {
                    var queued = false;
                    while (!queued && !timeout.IsCancellationRequested)
                    {
                        await using var verify = new WorkshopDbContext(osOptions);
                        queued = await verify.OutboxMessages.AnyAsync(item =>
                            item.EventType == "ExecutionRequested");
                        if (!queued) await Task.Delay(100);
                    }
                    Assert.True(queued);
                }
                await using (var dispatch = new WorkshopDbContext(osOptions))
                {
                    var sender = new SqsIntegrationMessageSender(sqs,
                        new Dictionary<string, string> { ["ExecutionRequested"] = commandsQueue });
                    Assert.Equal(1, await new OutboxDispatcher(dispatch, sender,
                        NullLogger<OutboxDispatcher>.Instance).DispatchBatchAsync(CancellationToken.None));
                }
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                {
                    while (state == WorkOrderProcessState.ReadyForExecution && !timeout.IsCancellationRequested)
                    {
                        await using var verify = new WorkshopDbContext(osOptions);
                        state = (await verify.WorkOrders.SingleAsync(item => item.Id == order.Id)).ProcessState;
                        if (state == WorkOrderProcessState.ReadyForExecution) await Task.Delay(100);
                    }
                }
                Assert.Equal(WorkOrderProcessState.InExecution, state);
            }
            else
            {
            var dynamoEndpoint = Environment.GetEnvironmentVariable("BILLING_DYNAMO_ENDPOINT")
                ?? throw new InvalidOperationException("Defina BILLING_DYNAMO_ENDPOINT para o teste local.");
            using var dynamo = new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"),
                new AmazonDynamoDBConfig { ServiceURL = dynamoEndpoint });
            var table = "billing_release_" + Guid.NewGuid().ToString("N");
            await dynamo.CreateTableAsync(new CreateTableRequest
            {
                TableName = table, BillingMode = BillingMode.PAY_PER_REQUEST,
                AttributeDefinitions = [new("pk", ScalarAttributeType.S), new("sk", ScalarAttributeType.S)],
                KeySchema = [new("pk", KeyType.HASH), new("sk", KeyType.RANGE)]
            });
            var billingSettings = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["DynamoDb:Table"] = table,
                    ["Messaging:OsEventsQueueUrl"] = osQueue
                }).Build();
            var billing = new Billing.BillingRepository(dynamo, billingSettings);
            var budget = Billing.Budget.Create(order.Id, 1, order.BranchId,
                [new Billing.BudgetLine(part.Id, part.Name, requested, 1000)]);
            await billing.CreateAsync(budget, CancellationToken.None);
            await billing.DecideAsync(order.Id, 1, true, CancellationToken.None);
            foreach (var item in await billing.GetPendingOutboxAsync(CancellationToken.None))
                await billing.MarkOutboxPublishedAsync(item.EventId, CancellationToken.None);
            var payment = new Billing.PaymentRequest(pendingOrder.PaymentAttemptId!.Value,
                order.Id, budget.Id, 1, order.BranchId, requested * 1000L, DateTime.UtcNow);
            await billing.RecordPaymentRequestAsync(Guid.NewGuid(), payment, CancellationToken.None);
            await billing.CompletePaymentCreationAsync(payment.Id,
                new Billing.MercadoPagoQrOrder("ORDTEST123", "qr-test", "created"),
                CancellationToken.None);
            var amount = (requested * 10m).ToString("F2",
                System.Globalization.CultureInfo.InvariantCulture);
            using var providerHttp = new HttpClient(new StubHandler(request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        id = "ORDTEST123", external_reference = $"pay_{payment.Id:N}",
                        status = rejectionReason, currency = "BRL", total_amount = amount,
                        transactions = new { payments = new[] { new { status = rejectionReason } } }
                    }), Encoding.UTF8, "application/json")
                };
            })) { BaseAddress = new Uri("https://api.mercadopago.com/") };
            var reconciler = new Billing.PaymentReconciliationService(billing,
                new Billing.MercadoPagoQrClient(providerHttp,
                    new Billing.MercadoPagoQrOptions("test-token", "test-pos")));
            Assert.True(await reconciler.ReconcileAsync("ORDTEST123", CancellationToken.None));
            Assert.Equal("Rejected", (await billing.GetPaymentAttemptAsync(payment.Id,
                CancellationToken.None))!.Status);
            using var billingServices = new ServiceCollection()
                .AddScoped(_ => new Billing.BillingRepository(dynamo, billingSettings))
                .BuildServiceProvider();
            using var billingOutbox = new Billing.BillingOutboxWorker(sqs,
                billingServices.GetRequiredService<IServiceScopeFactory>(), billingSettings,
                NullLogger<Billing.BillingOutboxWorker>.Instance);
            await billingOutbox.StartAsync(CancellationToken.None);
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                while (state == WorkOrderProcessState.AwaitingPayment && !timeout.IsCancellationRequested)
                {
                    await using var verify = new WorkshopDbContext(osOptions);
                    state = (await verify.WorkOrders.SingleAsync(item => item.Id == order.Id)).ProcessState;
                    if (state == WorkOrderProcessState.AwaitingPayment) await Task.Delay(100);
                }
            }
            Assert.Equal(WorkOrderProcessState.ReleasingStock, state);
            await using (var dispatch = new WorkshopDbContext(osOptions))
            {
                var paymentCommand = await dispatch.OutboxMessages
                    .SingleAsync(item => item.EventType == "CreatePaymentRequested");
                paymentCommand.MarkPublished(DateTime.UtcNow);
                await dispatch.SaveChangesAsync();
                var sender = new SqsIntegrationMessageSender(sqs,
                    new Dictionary<string, string> { ["ReleaseStockRequested"] = commandsQueue });
                Assert.Equal(1, await new OutboxDispatcher(dispatch, sender,
                    NullLogger<OutboxDispatcher>.Instance).DispatchBatchAsync(CancellationToken.None));
            }
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                while (state == WorkOrderProcessState.ReleasingStock && !timeout.IsCancellationRequested)
                {
                    await using var verify = new WorkshopDbContext(osOptions);
                    state = (await verify.WorkOrders.SingleAsync(item => item.Id == order.Id)).ProcessState;
                    if (state == WorkOrderProcessState.ReleasingStock) await Task.Delay(100);
                }
            }
            await billingOutbox.StopAsync(CancellationToken.None);
            }
        }
        await commandsWorker.StopAsync(CancellationToken.None);
        await executionOutbox.StopAsync(CancellationToken.None);
        await osWorker.StopAsync(CancellationToken.None);

        await using var finalOs = new WorkshopDbContext(osOptions);
        await using var finalExecution = new ExecutionDbContext(executionOptions);
        if (paid)
        {
            var consume = await finalOs.OutboxMessages.SingleAsync(item =>
                item.EventType == "ConsumeStockRequested");
            Assert.False(await new ConsumeStockRequestedHandler(finalExecution,
                new ExecutionService(finalExecution)).HandleAsync(consume.PayloadJson,
                CancellationToken.None));
            var consumed = await finalExecution.OutboxMessages.SingleAsync(item =>
                item.EventType == "StockConsumed");
            Assert.False(await new StockConsumedHandler(finalOs).HandleAsync(consumed.Body,
                CancellationToken.None));
        }
        else if (expectedState == WorkOrderProcessState.AwaitingPayment)
        {
            var release = await finalOs.OutboxMessages.SingleAsync(item =>
                item.EventType == "ReleaseStockRequested");
            Assert.False(await new ReleaseStockRequestedHandler(finalExecution,
                new ExecutionService(finalExecution)).HandleAsync(release.PayloadJson,
                CancellationToken.None));
            var released = await finalExecution.OutboxMessages.SingleAsync(item =>
                item.EventType == "StockReleased");
            Assert.False(await new StockReleasedHandler(finalOs).HandleAsync(released.Body,
                CancellationToken.None));
        }
        Assert.Equal(paid ? WorkOrderProcessState.InExecution : WorkOrderProcessState.Canceled, state);
        Assert.Equal(paid ? expectedStock - requested : expectedStock,
            (await finalExecution.PartSupplies.SingleAsync(item => item.Id == part.Id)).StockQuantity);
        if (paid)
            Assert.Equal(ReservationStatus.Consumed,
                (await finalExecution.StockReservations.SingleAsync()).Status);
        Assert.Equal(paid ? 4 : expectedState == WorkOrderProcessState.AwaitingPayment ? 3 : 1,
            await finalOs.InboxMessages.CountAsync());
        Assert.Equal(paid ? 3 : expectedState == WorkOrderProcessState.AwaitingPayment ? 2 : 1,
            await finalExecution.InboxMessages.CountAsync());
        Assert.Equal(paid ? 3 : expectedState == WorkOrderProcessState.AwaitingPayment ? 2 : 1,
            await finalExecution.OutboxMessages.CountAsync());
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}

