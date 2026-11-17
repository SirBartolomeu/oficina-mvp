extern alias execution;

using System.Net;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ExecutionCommandsWorker = execution::Oficina.Execution.ExecutionCommandsWorker;
using ExecutionOutboxWorker = execution::Oficina.Execution.ExecutionOutboxWorker;
using ExecutionService = execution::Oficina.Execution.ExecutionService;
using ReserveStockRequestedHandler = execution::Oficina.Execution.ReserveStockRequestedHandler;
using ConsumeStockRequestedHandler = execution::Oficina.Execution.ConsumeStockRequestedHandler;
using ExecutionRequestedHandler = execution::Oficina.Execution.ExecutionRequestedHandler;
using ExecutionDbContext = execution::Oficina.Execution.Persistence.ExecutionDbContext;
using ReservationStatus = execution::Oficina.Execution.Persistence.ReservationStatus;
using Oficina.Billing;
using OficinaMvp.Api.Messaging;
using OficinaMvp.Domain.Entities;
using OsEvents = OficinaMvp.Application.Contracts;
using OficinaMvp.Infrastructure.Messaging;
using OficinaMvp.Infrastructure.Persistence;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Xunit;

namespace Oficina.Messaging.Local.Tests;

public sealed class OpenedEventFlowTests
{
    [Fact]
    public async Task OpenedOrderMovesThroughApprovalPaymentAndExecution()
    {
        var spans = new ConcurrentBag<(ActivityTraceId TraceId, string Source)>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is SqsTraceContext.SourceName or
                "Oficina.Billing.Sqs" or "Oficina.Execution.Sqs",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => spans.Add((activity.TraceId, activity.Source.Name))
        };
        ActivitySource.AddActivityListener(listener);
        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_TEST_ENDPOINT");
        using var tracer = string.IsNullOrWhiteSpace(otlpEndpoint) ? null :
            Sdk.CreateTracerProviderBuilder()
                .ConfigureResource(resource => resource.AddService("oficina-saga-local-test"))
                .AddSource(SqsTraceContext.SourceName, "Oficina.Billing.Sqs", "Oficina.Execution.Sqs")
                .SetSampler(new AlwaysOnSampler())
                .AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint))
                .Build();
        using var sagaTrace = new Activity("local saga").SetIdFormat(ActivityIdFormat.W3C).Start();
        var sqsEndpoint = Environment.GetEnvironmentVariable("MESSAGING_SQS_ENDPOINT")
            ?? throw new InvalidOperationException("Defina MESSAGING_SQS_ENDPOINT para o teste local.");
        var dynamoEndpoint = Environment.GetEnvironmentVariable("BILLING_DYNAMO_ENDPOINT")
            ?? throw new InvalidOperationException("Defina BILLING_DYNAMO_ENDPOINT para o teste local.");
        using var sqs = new AmazonSQSClient(new BasicAWSCredentials("local", "local"),
            new AmazonSQSConfig { ServiceURL = sqsEndpoint });
        using var dynamo = new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = dynamoEndpoint });
        var queueUrl = (await sqs.CreateQueueAsync("billing-test-" + Guid.NewGuid().ToString("N"))).QueueUrl;
        var osQueueUrl = (await sqs.CreateQueueAsync("os-events-test-" + Guid.NewGuid().ToString("N"))).QueueUrl;
        var executionQueueUrl = (await sqs.CreateQueueAsync("execution-flow-" + Guid.NewGuid().ToString("N"))).QueueUrl;
        var table = "billing_flow_test_" + Guid.NewGuid().ToString("N");
        await dynamo.CreateTableAsync(new CreateTableRequest
        {
            TableName = table,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions = [new("pk", ScalarAttributeType.S), new("sk", ScalarAttributeType.S)],
            KeySchema = [new("pk", KeyType.HASH), new("sk", KeyType.RANGE)]
        });

        var customer = new Customer("Cliente Teste", "52998224725", null, null);
        var vehicle = new Vehicle(customer.Id, "BRA2E19", "VW", "Gol", 2020);
        var serviceId = Guid.NewGuid();
        var order = new WorkOrder(customer.Id, vehicle.Id,
            [new WorkOrderServiceLine(serviceId, "Revisão", 1, 125m)],
            [], null, DateTime.UtcNow, "sp-centro");
        var orderId = order.Id;
        var eventId = Guid.NewGuid();
        var envelope = new OsEvents.IntegrationEnvelope<OsEvents.WorkOrderOpenedPayload>(eventId, "WorkOrderOpened", 1,
            orderId, Guid.NewGuid(), orderId, "sp-centro", DateTime.UtcNow,
            new OsEvents.WorkOrderOpenedPayload(1,
                [new OsEvents.WorkOrderOpenedLine("Service", serviceId, "Revisão", 1, 12500)],
                12500, "BRL", DateTime.UtcNow));
        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var osConnectionString = $"Data Source=os-flow-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var osAnchor = new SqliteConnection(osConnectionString);
        await osAnchor.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<WorkshopDbContext>()
            .UseSqlite(osConnectionString).Options;
        await using var db = new WorkshopDbContext(dbOptions);
        await db.Database.EnsureCreatedAsync();
        var executionConnectionString = $"Data Source=execution-flow-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var executionAnchor = new SqliteConnection(executionConnectionString);
        await executionAnchor.OpenAsync();
        var executionOptions = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseSqlite(executionConnectionString).Options;
        await using (var executionSetup = new ExecutionDbContext(executionOptions))
            await executionSetup.Database.EnsureCreatedAsync();
        db.AddRange(customer, vehicle, order);
        ActivityTraceId originTraceId;
        using (var request = new Activity("open order").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            originTraceId = request.TraceId;
            db.OutboxMessages.Add(new OutboxMessage(eventId, "WorkOrderOpened", json, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        var senderWithFailure = new AckLostOnceSender(new SqsIntegrationMessageSender(sqs, queueUrl));
        var clock = new ManualTimeProvider();
        var dispatcher = new OutboxDispatcher(db, senderWithFailure,
            NullLogger<OutboxDispatcher>.Instance, clock);
        Assert.Equal(1, await dispatcher.DispatchBatchAsync(CancellationToken.None));
        Assert.Null((await db.OutboxMessages.SingleAsync()).PublishedAtUtc);
        Assert.Equal(1, (await db.OutboxMessages.SingleAsync()).PublishAttempts);
        Assert.Equal(0, await dispatcher.DispatchBatchAsync(CancellationToken.None));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, await dispatcher.DispatchBatchAsync(CancellationToken.None));
        Assert.NotNull((await db.OutboxMessages.SingleAsync()).PublishedAtUtc);
        Assert.Equal(2, (await db.OutboxMessages.SingleAsync()).PublishAttempts);
        Assert.Null((await db.OutboxMessages.SingleAsync()).LastError);
        Assert.Equal(2, senderWithFailure.Calls);
        var settings = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DynamoDb:Table"] = table,
                ["Messaging:WorkOrdersQueueUrl"] = queueUrl,
                ["Messaging:OsEventsQueueUrl"] = osQueueUrl,
                ["Messaging:CommandsQueueUrl"] = executionQueueUrl
            }).Build();
        var repository = new BillingRepository(dynamo, settings);
        using var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(settings)
            .AddSingleton<IAmazonDynamoDB>(dynamo)
            .AddScoped<BillingRepository>()
            .AddScoped<IBudgetEventStore>(provider => provider.GetRequiredService<BillingRepository>())
            .AddScoped<WorkOrderOpenedHandler>()
            .AddScoped<PaymentRequestHandler>()
            .BuildServiceProvider();
        using var worker = new BillingSqsWorker(sqs, services.GetRequiredService<IServiceScopeFactory>(),
            settings, NullLogger<BillingSqsWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        Budget? budget = null;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            while (budget is null && !timeout.IsCancellationRequested)
            {
                budget = await repository.GetAsync(orderId, 1, CancellationToken.None);
                if (budget is null) await Task.Delay(100);
            }
        }
        Assert.NotNull(budget);
        var duplicate = await new WorkOrderOpenedHandler(repository).HandleAsync(json, CancellationToken.None);
        var ready = Assert.Single(await repository.GetPendingOutboxAsync(CancellationToken.None));
        using (var readyBody = JsonDocument.Parse(ready.Body))
        {
            var parent = readyBody.RootElement.GetProperty("traceparent").GetString();
            Assert.True(ActivityContext.TryParse(parent, null, out var context));
            Assert.Equal(originTraceId, context.TraceId);
        }

        using var osServices = new ServiceCollection()
            .AddScoped(_ => new WorkshopDbContext(dbOptions))
            .AddScoped<BudgetReadyHandler>()
            .AddScoped<BudgetDecisionHandler>()
            .AddScoped<StockReservationResultHandler>()
            .AddScoped<PaymentConfirmedHandler>()
            .AddScoped<StockConsumedHandler>()
            .AddScoped<ExecutionStartedHandler>()
            .BuildServiceProvider();
        using var executionServices = new ServiceCollection()
            .AddScoped(_ => new ExecutionDbContext(executionOptions))
            .AddScoped<ExecutionService>()
            .AddScoped<ReserveStockRequestedHandler>()
            .AddScoped<ConsumeStockRequestedHandler>()
            .AddScoped<ExecutionRequestedHandler>()
            .BuildServiceProvider();
        using var billingOutbox = new BillingOutboxWorker(sqs,
            services.GetRequiredService<IServiceScopeFactory>(), settings,
            NullLogger<BillingOutboxWorker>.Instance);
        using var osWorker = new OsEventsSqsWorker(sqs,
            osServices.GetRequiredService<IServiceScopeFactory>(), settings,
            NullLogger<OsEventsSqsWorker>.Instance);
        using var executionWorker = new ExecutionCommandsWorker(sqs,
            executionServices.GetRequiredService<IServiceScopeFactory>(), settings,
            NullLogger<ExecutionCommandsWorker>.Instance);
        using var executionOutbox = new ExecutionOutboxWorker(sqs,
            executionServices.GetRequiredService<IServiceScopeFactory>(), settings,
            NullLogger<ExecutionOutboxWorker>.Instance);
        await billingOutbox.StartAsync(CancellationToken.None);
        await osWorker.StartAsync(CancellationToken.None);
        await executionWorker.StartAsync(CancellationToken.None);
        await executionOutbox.StartAsync(CancellationToken.None);
        WorkOrderStatus status = WorkOrderStatus.Received;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            while (status != WorkOrderStatus.AwaitingApproval && !timeout.IsCancellationRequested)
            {
                await using var verify = new WorkshopDbContext(dbOptions);
                status = (await verify.WorkOrders.SingleAsync(item => item.Id == orderId)).Status;
                if (status != WorkOrderStatus.AwaitingApproval)
                    await Task.Delay(100);
            }
        }
        Assert.Equal(12500, budget.TotalCents);
        Assert.Equal(budget.WorkOrderId, duplicate.WorkOrderId);
        Assert.NotNull(await repository.GetAsync(orderId, 1, CancellationToken.None));
        Assert.Equal(WorkOrderStatus.AwaitingApproval, status);
        await using var finalDb = new WorkshopDbContext(dbOptions);
        Assert.Equal(budget.Id, (await finalDb.WorkOrders.SingleAsync(item => item.Id == orderId)).BudgetId);
        Assert.Single(await finalDb.InboxMessages.ToListAsync());
        Assert.False(await new BudgetReadyHandler(finalDb).HandleAsync(ready.Body, CancellationToken.None));
        Assert.Empty(await repository.GetPendingOutboxAsync(CancellationToken.None));

        var approved = await repository.DecideAsync(orderId, 1, true, CancellationToken.None);
        Assert.Equal(BudgetStatus.Approved, approved!.Status);
        Assert.Equal(BudgetStatus.Approved,
            (await repository.DecideAsync(orderId, 1, true, CancellationToken.None))!.Status);
        WorkOrderProcessState decisionState = WorkOrderProcessState.AwaitingApproval;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            while (decisionState == WorkOrderProcessState.AwaitingApproval && !timeout.IsCancellationRequested)
            {
                await using var verify = new WorkshopDbContext(dbOptions);
                decisionState = (await verify.WorkOrders.SingleAsync(item => item.Id == orderId)).ProcessState;
                if (decisionState == WorkOrderProcessState.AwaitingApproval)
                    await Task.Delay(100);
            }
        }
        await using var decisionDb = new WorkshopDbContext(dbOptions);
        var decidedOrder = await decisionDb.WorkOrders.SingleAsync(item => item.Id == orderId);
        Assert.Equal(WorkOrderProcessState.AwaitingStockReservation, decisionState);
        Assert.Equal(decisionState, decidedOrder.ProcessState);
        Assert.Equal(2, await decisionDb.InboxMessages.CountAsync());
        var reserveCommand = Assert.Single(await decisionDb.OutboxMessages
            .Where(item => item.EventType == "ReserveStockRequested").ToListAsync());
        Assert.Null(reserveCommand.PublishedAtUtc);
        await using (var dispatchDb = new WorkshopDbContext(dbOptions))
        {
            var sender = new SqsIntegrationMessageSender(sqs,
                new Dictionary<string, string> { ["ReserveStockRequested"] = executionQueueUrl });
            Assert.Equal(1, await new OutboxDispatcher(dispatchDb, sender,
                NullLogger<OutboxDispatcher>.Instance).DispatchBatchAsync(CancellationToken.None));
        }
        Assert.Empty(await repository.GetPendingOutboxAsync(CancellationToken.None));
        WorkOrderProcessState reservationState = WorkOrderProcessState.AwaitingStockReservation;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            while (reservationState == WorkOrderProcessState.AwaitingStockReservation &&
                   !timeout.IsCancellationRequested)
            {
                await using var verify = new WorkshopDbContext(dbOptions);
                reservationState = (await verify.WorkOrders.SingleAsync(item => item.Id == orderId)).ProcessState;
                if (reservationState == WorkOrderProcessState.AwaitingStockReservation)
                    await Task.Delay(100);
            }
        }
        Assert.Equal(WorkOrderProcessState.AwaitingPayment, reservationState);
        var paymentAttemptId = (await decisionDb.WorkOrders.AsNoTracking()
            .SingleAsync(item => item.Id == orderId)).PaymentAttemptId!.Value;
        await using (var dispatchDb = new WorkshopDbContext(dbOptions))
        {
            var sender = new SqsIntegrationMessageSender(sqs,
                new Dictionary<string, string> { ["CreatePaymentRequested"] = queueUrl });
            Assert.Equal(1, await new OutboxDispatcher(dispatchDb, sender,
                NullLogger<OutboxDispatcher>.Instance).DispatchBatchAsync(CancellationToken.None));
        }
        PaymentRequest? payment = null;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            while (payment is null && !timeout.IsCancellationRequested)
            {
                payment = await repository.GetPaymentRequestAsync(paymentAttemptId, CancellationToken.None);
                if (payment is null) await Task.Delay(100);
            }
        }
        await worker.StopAsync(CancellationToken.None);
        Assert.NotNull(payment);
        Assert.Equal(12500, payment.TotalCents);
        Assert.Equal(budget.Id, payment.BudgetId);
        Assert.True(await repository.CompletePaymentCreationAsync(paymentAttemptId,
            new MercadoPagoQrOrder("ORDTEST123", "qr-test", "created"), CancellationToken.None));
        var wrongAmount = new OsEvents.IntegrationEnvelope<OsEvents.PaymentConfirmedPayload>(
            Guid.NewGuid(), "PaymentConfirmed", 1, orderId, Guid.NewGuid(), orderId,
            order.BranchId, DateTime.UtcNow,
            new OsEvents.PaymentConfirmedPayload(paymentAttemptId, "ORDTEST123", 12499, "BRL"));
        await using (var invalidDb = new WorkshopDbContext(dbOptions))
        {
            await Assert.ThrowsAsync<OficinaMvp.Domain.Exceptions.DomainException>(() =>
                new PaymentConfirmedHandler(invalidDb).HandleAsync(
                    JsonSerializer.Serialize(wrongAmount,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    CancellationToken.None));
            Assert.Equal(WorkOrderProcessState.AwaitingPayment,
                (await invalidDb.WorkOrders.SingleAsync(item => item.Id == orderId))
                .ProcessState);
            Assert.False(await invalidDb.OutboxMessages.AnyAsync(item =>
                item.EventType == "ConsumeStockRequested"));
            Assert.False(await invalidDb.InboxMessages.AnyAsync(item =>
                item.EventType == "PaymentConfirmed"));
        }
        using var providerHttp = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    id = "ORDTEST123", external_reference = $"pay_{payment.Id:N}",
                    status = "processed", currency = "BRL", total_amount = "125.00",
                    transactions = new { payments = new[]
                    {
                        new { status = "processed", paid_amount = "125.00" }
                    } }
                }), Encoding.UTF8, "application/json")
            };
        })) { BaseAddress = new Uri("https://api.mercadopago.com/") };
        var reconciler = new PaymentReconciliationService(repository,
            new MercadoPagoQrClient(providerHttp,
                new MercadoPagoQrOptions("test-token", "test-pos")));
        Assert.True(await reconciler.ReconcileAsync("ORDTEST123", CancellationToken.None));
        Assert.False(await reconciler.ReconcileAsync("ORDTEST123", CancellationToken.None));
        WorkOrderProcessState paymentState = WorkOrderProcessState.AwaitingPayment;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            while (paymentState == WorkOrderProcessState.AwaitingPayment && !timeout.IsCancellationRequested)
            {
                await using var verify = new WorkshopDbContext(dbOptions);
                paymentState = (await verify.WorkOrders.SingleAsync(item => item.Id == orderId)).ProcessState;
                if (paymentState == WorkOrderProcessState.AwaitingPayment)
                    await Task.Delay(100);
            }
        }
        await billingOutbox.StopAsync(CancellationToken.None);
        Assert.Equal(WorkOrderProcessState.ReadyForExecution, paymentState);
        await using (var consumeDb = new WorkshopDbContext(dbOptions))
        {
            var sender = new SqsIntegrationMessageSender(sqs,
                new Dictionary<string, string> { ["ConsumeStockRequested"] = executionQueueUrl });
            Assert.Equal(1, await new OutboxDispatcher(consumeDb, sender,
                NullLogger<OutboxDispatcher>.Instance).DispatchBatchAsync(CancellationToken.None));
        }
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            var queued = false;
            while (!queued && !timeout.IsCancellationRequested)
            {
                await using var verify = new WorkshopDbContext(dbOptions);
                queued = await verify.OutboxMessages.AnyAsync(item =>
                    item.EventType == "ExecutionRequested" && item.PublishedAtUtc == null);
                if (!queued) await Task.Delay(100);
            }
            Assert.True(queued);
        }
        await using (var executionDb = new WorkshopDbContext(dbOptions))
        {
            var sender = new SqsIntegrationMessageSender(sqs,
                new Dictionary<string, string> { ["ExecutionRequested"] = executionQueueUrl });
            Assert.Equal(1, await new OutboxDispatcher(executionDb, sender,
                NullLogger<OutboxDispatcher>.Instance).DispatchBatchAsync(CancellationToken.None));
        }
        WorkOrderProcessState executionState = WorkOrderProcessState.ReadyForExecution;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            while (executionState == WorkOrderProcessState.ReadyForExecution &&
                   !timeout.IsCancellationRequested)
            {
                await using var verify = new WorkshopDbContext(dbOptions);
                executionState = (await verify.WorkOrders.SingleAsync(item => item.Id == orderId)).ProcessState;
                if (executionState == WorkOrderProcessState.ReadyForExecution)
                    await Task.Delay(100);
            }
        }
        await executionWorker.StopAsync(CancellationToken.None);
        await executionOutbox.StopAsync(CancellationToken.None);
        await osWorker.StopAsync(CancellationToken.None);
        Assert.Equal(WorkOrderProcessState.InExecution, executionState);
        await using var paymentDb = new WorkshopDbContext(dbOptions);
        Assert.Equal(WorkOrderStatus.InExecution,
            (await paymentDb.WorkOrders.SingleAsync(item => item.Id == orderId)).Status);
        Assert.Equal(6, await paymentDb.InboxMessages.CountAsync());
        var paymentEventId = (await paymentDb.InboxMessages.SingleAsync(item =>
            item.EventType == "PaymentConfirmed")).EventId;
        var duplicatePayment = new OsEvents.IntegrationEnvelope<OsEvents.PaymentConfirmedPayload>(
            paymentEventId, "PaymentConfirmed", 1, orderId, Guid.NewGuid(), orderId,
            order.BranchId, DateTime.UtcNow,
            new OsEvents.PaymentConfirmedPayload(paymentAttemptId, "ORDTEST123", 12500, "BRL"));
        var duplicateQueueUrl = (await sqs.CreateQueueAsync(
            "os-duplicate-test-" + Guid.NewGuid().ToString("N"))).QueueUrl;
        await sqs.SendMessageAsync(duplicateQueueUrl, JsonSerializer.Serialize(duplicatePayment,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var requeued = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = duplicateQueueUrl, MaxNumberOfMessages = 1, WaitTimeSeconds = 5
        });
        var message = Assert.Single(requeued.Messages);
        Assert.False(await new PaymentConfirmedHandler(paymentDb).HandleAsync(message.Body,
            CancellationToken.None));
        await sqs.DeleteMessageAsync(duplicateQueueUrl, message.ReceiptHandle);
        Assert.Equal(6, await paymentDb.InboxMessages.CountAsync());
        Assert.Single(await paymentDb.OutboxMessages.Where(item =>
            item.EventType == "ConsumeStockRequested").ToListAsync());
        await using var finalExecution = new ExecutionDbContext(executionOptions);
        Assert.Equal(ReservationStatus.Consumed,
            (await finalExecution.StockReservations.SingleAsync()).Status);
        Assert.Contains(spans, span => span.TraceId == sagaTrace.TraceId &&
            span.Source == SqsTraceContext.SourceName);
        Assert.Contains(spans, span => span.TraceId == sagaTrace.TraceId &&
            span.Source == "Oficina.Billing.Sqs");
        Assert.Contains(spans, span => span.TraceId == sagaTrace.TraceId &&
            span.Source == "Oficina.Execution.Sqs");
        Assert.True(tracer?.ForceFlush() ?? true);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    private sealed class AckLostOnceSender(IIntegrationMessageSender inner) : IIntegrationMessageSender
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public async Task SendAsync(Guid eventId, string eventType, string payloadJson,
            CancellationToken cancellationToken)
        {
            await inner.SendAsync(eventId, eventType, payloadJson, cancellationToken);
            if (Interlocked.Increment(ref _calls) == 1)
                throw new HttpRequestException("Confirmação SQS perdida após o envio.");
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
