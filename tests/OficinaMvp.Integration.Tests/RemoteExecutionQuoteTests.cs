using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Application.Ports;
using OficinaMvp.Infrastructure.Persistence;
using OficinaMvp.Infrastructure.Messaging;
using OficinaMvp.Domain.Exceptions;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Integration.Tests.Infrastructure;

namespace OficinaMvp.Integration.Tests;

public sealed class RemoteExecutionQuoteTests
{
    [Fact]
    public async Task WorkOrderUsesRemoteCatalogSnapshotWithoutLocalCatalogRows()
    {
        var serviceId = Guid.NewGuid();
        var partId = Guid.NewGuid();
        using var factory = new CustomWebApplicationFactory
        {
            ExecutionQuoteClient = new StubQuoteClient((_, _) => new ExecutionQuoteResponse(
                [new ExecutionQuotedItem(serviceId, "Diagnóstico remoto", 1, 12500)],
                [new ExecutionQuotedItem(partId, "Filtro remoto", 2, 3250)],
                19000))
        };
        using var client = factory.CreateClient();
        await AuthenticateAndCreateCustomerAsync(client);

        var response = await client.PostAsJsonAsync("/api/work-orders", NewRequest(serviceId, partId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(190m, order.GetProperty("budgetTotal").GetDecimal());
        Assert.Equal("Diagnóstico remoto", order.GetProperty("services")[0].GetProperty("serviceName").GetString());
        Assert.Equal("Filtro remoto", order.GetProperty("parts")[0].GetProperty("partName").GetString());
        using var scope = factory.Services.CreateScope();
        var outbox = Assert.Single(await scope.ServiceProvider.GetRequiredService<WorkshopDbContext>()
            .OutboxMessages.AsNoTracking().ToListAsync());
        Assert.Null(outbox.PublishedAtUtc);
        Assert.DoesNotContain("52998224725", outbox.PayloadJson);
        using var envelope = JsonDocument.Parse(outbox.PayloadJson);
        Assert.Equal("WorkOrderOpened", envelope.RootElement.GetProperty("eventType").GetString());
        Assert.Equal(order.GetProperty("id").GetGuid(), envelope.RootElement.GetProperty("workOrderId").GetGuid());
        Assert.Equal(19000, envelope.RootElement.GetProperty("payload").GetProperty("totalCents").GetInt64());
        Assert.Equal(2, envelope.RootElement.GetProperty("payload").GetProperty("lines").GetArrayLength());

        var orderId = order.GetProperty("id").GetGuid();
        var readyEventId = Guid.NewGuid();
        var budgetId = Guid.NewGuid();
        var ready = new IntegrationEnvelope<BudgetReadyPayload>(readyEventId, "BudgetReady", 1,
            orderId, outbox.EventId, orderId, null, DateTime.UtcNow,
            new BudgetReadyPayload(budgetId, 1, 19000, "BRL"));
        var readyJson = JsonSerializer.Serialize(ready, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var handler = new BudgetReadyHandler(scope.ServiceProvider.GetRequiredService<WorkshopDbContext>());
        Assert.True(await handler.HandleAsync(readyJson, CancellationToken.None));
        Assert.False(await handler.HandleAsync(readyJson, CancellationToken.None));
        Assert.Single(await scope.ServiceProvider.GetRequiredService<WorkshopDbContext>()
            .InboxMessages.AsNoTracking().ToListAsync());

        var directApproval = await client.PostAsJsonAsync($"/api/work-orders/{orderId}/approve-budget", new { });
        Assert.Equal(HttpStatusCode.BadRequest, directApproval.StatusCode);

        var decisionId = Guid.NewGuid();
        var decision = new IntegrationEnvelope<BudgetDecisionPayload>(decisionId, "BudgetApproved", 1,
            orderId, readyEventId, orderId, null, DateTime.UtcNow,
            new BudgetDecisionPayload(budgetId, 1, decisionId, null));
        var decisionBody = JsonSerializer.Serialize(decision, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using (var decisionScope = factory.Services.CreateScope())
        {
            var decisionHandler = new BudgetDecisionHandler(
                decisionScope.ServiceProvider.GetRequiredService<WorkshopDbContext>());
            Assert.True(await decisionHandler.HandleAsync(decisionBody, CancellationToken.None));
            Assert.False(await decisionHandler.HandleAsync(decisionBody, CancellationToken.None));
        }
        var db = scope.ServiceProvider.GetRequiredService<WorkshopDbContext>();
        var approved = await db.WorkOrders.AsNoTracking().SingleAsync(item => item.Id == orderId);
        Assert.Equal(WorkOrderProcessState.AwaitingStockReservation, approved.ProcessState);
        Assert.NotNull(approved.ReservationId);
        var reserve = Assert.Single(await db.OutboxMessages.AsNoTracking()
            .Where(item => item.EventType == "ReserveStockRequested").ToListAsync());
        using var reserveJson = JsonDocument.Parse(reserve.PayloadJson);
        Assert.Equal(2, reserveJson.RootElement.GetProperty("payload")
            .GetProperty("parts")[0].GetProperty("quantity").GetInt32());
        var reserved = new IntegrationEnvelope<StockReservationResultPayload>(
            Guid.NewGuid(), "StockReserved", 1, orderId, reserve.EventId, orderId, null,
            DateTime.UtcNow, new StockReservationResultPayload(approved.ReservationId!.Value, 1, null));
        var reservedBody = JsonSerializer.Serialize(reserved, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var reservationScope = factory.Services.CreateScope();
        var resultDb = reservationScope.ServiceProvider.GetRequiredService<WorkshopDbContext>();
        var resultHandler = new StockReservationResultHandler(resultDb);
        Assert.True(await resultHandler.HandleAsync(reservedBody, CancellationToken.None));
        Assert.False(await resultHandler.HandleAsync(reservedBody, CancellationToken.None));
        Assert.Equal(WorkOrderProcessState.AwaitingPayment,
            (await resultDb.WorkOrders.AsNoTracking().SingleAsync(item => item.Id == orderId)).ProcessState);
        var paymentCommand = Assert.Single(await resultDb.OutboxMessages.AsNoTracking()
            .Where(item => item.EventType == "CreatePaymentRequested").ToListAsync());
        using (var paymentJson = JsonDocument.Parse(paymentCommand.PayloadJson))
        {
            Assert.Equal(19000, paymentJson.RootElement.GetProperty("payload")
                .GetProperty("totalCents").GetInt64());
            Assert.Equal((await resultDb.WorkOrders.AsNoTracking()
                    .SingleAsync(item => item.Id == orderId)).PaymentAttemptId,
                paymentJson.RootElement.GetProperty("payload")
                    .GetProperty("paymentAttemptId").GetGuid());
        }
        var updated = await client.GetFromJsonAsync<JsonElement>($"/api/work-orders/{orderId}");
        Assert.Equal("AwaitingApproval", updated.GetProperty("status").GetString());

        var paymentEvent = new IntegrationEnvelope<PaymentConfirmedPayload>(Guid.NewGuid(),
            "PaymentConfirmed", 1, orderId, paymentCommand.EventId, orderId, null,
            DateTime.UtcNow, new PaymentConfirmedPayload(
                (await resultDb.WorkOrders.AsNoTracking().SingleAsync(item => item.Id == orderId))
                    .PaymentAttemptId!.Value, "ORDTEST123", 19000, "BRL"));
        var paymentBody = JsonSerializer.Serialize(paymentEvent,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using (var paymentScope = factory.Services.CreateScope())
        {
            var paymentDb = paymentScope.ServiceProvider.GetRequiredService<WorkshopDbContext>();
            var paymentHandler = new PaymentConfirmedHandler(paymentDb);
            Assert.True(await paymentHandler.HandleAsync(paymentBody, CancellationToken.None));
            Assert.False(await paymentHandler.HandleAsync(paymentBody, CancellationToken.None));
            Assert.Equal(WorkOrderProcessState.ReadyForExecution,
                (await paymentDb.WorkOrders.AsNoTracking().SingleAsync(item => item.Id == orderId))
                    .ProcessState);
            var consumeCommand = Assert.Single(await paymentDb.OutboxMessages
                .Where(item => item.EventType == "ConsumeStockRequested").ToListAsync());
            var consumed = new IntegrationEnvelope<StockConsumedPayload>(Guid.NewGuid(),
                "StockConsumed", 1, orderId, consumeCommand.EventId, orderId, null,
                DateTime.UtcNow, new StockConsumedPayload(
                    (await paymentDb.WorkOrders.SingleAsync(item => item.Id == orderId))
                        .ReservationId!.Value));
            var consumedBody = JsonSerializer.Serialize(consumed,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var consumedHandler = new StockConsumedHandler(paymentDb);
            Assert.True(await consumedHandler.HandleAsync(consumedBody, CancellationToken.None));
            Assert.False(await consumedHandler.HandleAsync(consumedBody, CancellationToken.None));
            var executionCommand = Assert.Single(await paymentDb.OutboxMessages
                .Where(item => item.EventType == "ExecutionRequested").ToListAsync());
            Assert.Equal(WorkOrderProcessState.ReadyForExecution,
                (await paymentDb.WorkOrders.AsNoTracking().SingleAsync(item => item.Id == orderId))
                    .ProcessState);
            var startedEvent = new IntegrationEnvelope<ExecutionStartedPayload>(Guid.NewGuid(),
                "ExecutionStarted", 1, orderId, executionCommand.EventId, orderId, null,
                DateTime.UtcNow, new ExecutionStartedPayload(orderId,
                    (await paymentDb.WorkOrders.SingleAsync(item => item.Id == orderId))
                        .ReservationId!.Value,
                    (await paymentDb.WorkOrders.SingleAsync(item => item.Id == orderId))
                        .PaymentAttemptId!.Value));
            var startedBody = JsonSerializer.Serialize(startedEvent,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var startedHandler = new ExecutionStartedHandler(paymentDb);
            Assert.True(await startedHandler.HandleAsync(startedBody, CancellationToken.None));
            Assert.False(await startedHandler.HandleAsync(startedBody, CancellationToken.None));
            var started = await paymentDb.WorkOrders.AsNoTracking()
                .SingleAsync(item => item.Id == orderId);
            Assert.Equal(WorkOrderProcessState.InExecution, started.ProcessState);
            Assert.Equal(WorkOrderStatus.InExecution, started.Status);
            Assert.NotNull(started.ExecutionStartedAtUtc);
            var finishedEvent = new IntegrationEnvelope<ExecutionFinishedPayload>(Guid.NewGuid(),
                "ExecutionFinished", 1, orderId, Guid.NewGuid(), orderId, null,
                DateTime.UtcNow, new ExecutionFinishedPayload(orderId,
                    started.ReservationId!.Value, started.PaymentAttemptId!.Value));
            var finishedBody = JsonSerializer.Serialize(finishedEvent,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var finishedHandler = new ExecutionFinishedHandler(paymentDb);
            Assert.True(await finishedHandler.HandleAsync(finishedBody, CancellationToken.None));
            Assert.False(await finishedHandler.HandleAsync(finishedBody, CancellationToken.None));
            var finalized = await paymentDb.WorkOrders.AsNoTracking()
                .SingleAsync(item => item.Id == orderId);
            Assert.Equal(WorkOrderProcessState.Completed, finalized.ProcessState);
            Assert.Equal(WorkOrderStatus.Finalized, finalized.Status);
            Assert.NotNull(finalized.FinalizedAtUtc);
        }

        var invalid = ready with { EventId = Guid.NewGuid(),
            Payload = new BudgetReadyPayload(budgetId, 1, 9999, "BRL") };
        await Assert.ThrowsAsync<DomainException>(() => handler.HandleAsync(
            JsonSerializer.Serialize(invalid, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            CancellationToken.None));
        Assert.Equal(7, await resultDb.InboxMessages.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task BillingRefusalKeepsOrderAwaitingReviewWithoutReservingStock()
    {
        var serviceId = Guid.NewGuid();
        var partId = Guid.NewGuid();
        using var factory = new CustomWebApplicationFactory
        {
            ExecutionQuoteClient = new StubQuoteClient((_, _) => new ExecutionQuoteResponse(
                [new ExecutionQuotedItem(serviceId, "Revisão", 1, 12500)],
                [new ExecutionQuotedItem(partId, "Filtro", 2, 3250)], 19000))
        };
        using var client = factory.CreateClient();
        await AuthenticateAndCreateCustomerAsync(client);
        var response = await client.PostAsJsonAsync("/api/work-orders", NewRequest(serviceId, partId));
        response.EnsureSuccessStatusCode();
        var orderId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkshopDbContext>();
        var openedId = (await db.OutboxMessages.AsNoTracking().SingleAsync()).EventId;
        var budgetId = Guid.NewGuid();
        var ready = new IntegrationEnvelope<BudgetReadyPayload>(Guid.NewGuid(), "BudgetReady", 1,
            orderId, openedId, orderId, null, DateTime.UtcNow,
            new BudgetReadyPayload(budgetId, 1, 19000, "BRL"));
        Assert.True(await new BudgetReadyHandler(db).HandleAsync(
            JsonSerializer.Serialize(ready, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            CancellationToken.None));
        var decisionId = Guid.NewGuid();
        var refused = new IntegrationEnvelope<BudgetDecisionPayload>(decisionId, "BudgetRefused", 1,
            orderId, ready.EventId, orderId, null, DateTime.UtcNow,
            new BudgetDecisionPayload(budgetId, 1, decisionId, "Cliente recusou"));
        using var decisionScope = factory.Services.CreateScope();
        var decisionDb = decisionScope.ServiceProvider.GetRequiredService<WorkshopDbContext>();
        Assert.True(await new BudgetDecisionHandler(decisionDb).HandleAsync(
            JsonSerializer.Serialize(refused, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            CancellationToken.None));
        Assert.Equal(WorkOrderProcessState.AwaitingApproval,
            (await decisionDb.WorkOrders.AsNoTracking().SingleAsync(item => item.Id == orderId)).ProcessState);
        Assert.Empty(await decisionDb.OutboxMessages.AsNoTracking()
            .Where(item => item.EventType == "ReserveStockRequested").ToListAsync());
    }

    [Fact]
    public async Task WorkOrderRejectsInconsistentRemoteQuote()
    {
        var serviceId = Guid.NewGuid();
        var partId = Guid.NewGuid();
        using var factory = new CustomWebApplicationFactory
        {
            ExecutionQuoteClient = new StubQuoteClient((_, _) => new ExecutionQuoteResponse(
                [new ExecutionQuotedItem(serviceId, "Serviço", 1, 10000)],
                [new ExecutionQuotedItem(partId, "Peça", 2, 2500)],
                9999))
        };
        using var client = factory.CreateClient();
        await AuthenticateAndCreateCustomerAsync(client);

        var response = await client.PostAsJsonAsync("/api/work-orders", NewRequest(serviceId, partId));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var orders = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/work-orders");
        Assert.Empty(orders.EnumerateArray());
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<WorkshopDbContext>()
            .OutboxMessages.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task UnavailableExecutionReturnsGatewayErrorWithoutCreatingOrder()
    {
        using var factory = new CustomWebApplicationFactory
        {
            ExecutionQuoteClient = new FailingQuoteClient()
        };
        using var client = factory.CreateClient();
        await AuthenticateAndCreateCustomerAsync(client);

        var response = await client.PostAsJsonAsync("/api/work-orders", NewRequest(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var orders = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/work-orders");
        Assert.Empty(orders.EnumerateArray());
    }

    private static CreateWorkOrderRequest NewRequest(Guid serviceId, Guid partId) => new()
    {
        CustomerDocument = "52998224725",
        Vehicle = new VehicleInfo { LicensePlate = "BRA2E19", Brand = "VW", Model = "Gol", Year = 2020 },
        Services = [new RequestedService { ServiceId = serviceId, Quantity = 1 }],
        Parts = [new RequestedPart { PartId = partId, Quantity = 2 }]
    };

    private static async Task AuthenticateAndCreateCustomerAsync(HttpClient client)
    {
        var auth = await client.PostAsJsonAsync("/api/auth/token", new TokenRequest("admin", "Admin@123"));
        auth.EnsureSuccessStatusCode();
        var token = await auth.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        var customer = await client.PostAsJsonAsync("/api/customers",
            new UpsertCustomerRequest("Maria", "52998224725", "11999999999", "maria@example.com"));
        customer.EnsureSuccessStatusCode();
    }

    private sealed class StubQuoteClient(Func<IReadOnlyCollection<RequestedService>, IReadOnlyCollection<RequestedPart>, ExecutionQuoteResponse> quote)
        : IExecutionQuoteClient
    {
        public Task<ExecutionQuoteResponse> QuoteAsync(IReadOnlyCollection<RequestedService> services,
            IReadOnlyCollection<RequestedPart> parts, CancellationToken cancellationToken) =>
            Task.FromResult(quote(services, parts));
    }

    private sealed class FailingQuoteClient : IExecutionQuoteClient
    {
        public Task<ExecutionQuoteResponse> QuoteAsync(IReadOnlyCollection<RequestedService> services,
            IReadOnlyCollection<RequestedPart> parts, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Execution unavailable");
    }
}
