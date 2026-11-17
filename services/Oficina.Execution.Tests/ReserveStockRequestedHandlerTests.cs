using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Oficina.Execution;
using Oficina.Execution.Contracts;
using Oficina.Execution.Persistence;
using OficinaMvp.Domain.Entities;
using Xunit;

namespace Oficina.Execution.Tests;

public sealed class ReserveStockRequestedHandlerTests
{
    [Theory]
    [InlineData(2, 3, "StockReserved")]
    [InlineData(6, 5, "StockReservationRejected")]
    public async Task RequestProducesOneResultAndNeverPartiallyChangesStock(
        int requested, int expectedStock, string resultType)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ExecutionDbContext(
            new DbContextOptionsBuilder<ExecutionDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var part = new PartSupply("Filtro", 10m, 5);
        db.PartSupplies.Add(part);
        await db.SaveChangesAsync();
        var command = new IntegrationEnvelope<ReserveStockRequestedPayload>(
            Guid.NewGuid(), "ReserveStockRequested", 1, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "sp-centro", DateTime.UtcNow,
            new ReserveStockRequestedPayload(Guid.NewGuid(), 1,
                [new CatalogItemRequest(part.Id, requested)], DateTime.UtcNow.AddMinutes(15)));
        var body = JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var handler = new ReserveStockRequestedHandler(db, new ExecutionService(db));

        Assert.True(await handler.HandleAsync(body, CancellationToken.None));
        Assert.False(await handler.HandleAsync(body, CancellationToken.None));

        Assert.Equal(expectedStock, (await db.PartSupplies.SingleAsync()).StockQuantity);
        Assert.Single(await db.InboxMessages.ToListAsync());
        var response = Assert.Single(await db.OutboxMessages.ToListAsync());
        Assert.Equal(resultType, response.EventType);
        Assert.Equal(resultType == "StockReserved" ? 1 : 0,
            await db.StockReservations.CountAsync());
    }

    [Fact]
    public async Task ReleaseCommandReturnsStockOnceAndPublishesConfirmation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ExecutionDbContext(
            new DbContextOptionsBuilder<ExecutionDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var part = new PartSupply("Filtro", 10m, 5);
        db.PartSupplies.Add(part);
        await db.SaveChangesAsync();
        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var service = new ExecutionService(db);
        await service.ReserveAsync(new ReserveStockRequest(reservationId, orderId,
            [new CatalogItemRequest(part.Id, 2)]), CancellationToken.None);
        var command = new IntegrationEnvelope<ReleaseStockRequestedPayload>(
            Guid.NewGuid(), "ReleaseStockRequested", 1, orderId, Guid.NewGuid(),
            orderId, "sp-centro", DateTime.UtcNow,
            new ReleaseStockRequestedPayload(reservationId, "expired"));
        var body = JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var handler = new ReleaseStockRequestedHandler(db, service);

        Assert.True(await handler.HandleAsync(body, CancellationToken.None));
        Assert.False(await handler.HandleAsync(body, CancellationToken.None));
        Assert.Equal(5, (await db.PartSupplies.SingleAsync()).StockQuantity);
        Assert.Equal(ReservationStatus.Released,
            (await db.StockReservations.SingleAsync()).Status);
        Assert.Single(await db.OutboxMessages.ToListAsync());
        Assert.Equal("StockReleased", (await db.OutboxMessages.SingleAsync()).EventType);
        Assert.Single(await db.InboxMessages.ToListAsync());
    }

    [Fact]
    public async Task ConsumeCommandMarksReservationAndPublishesConfirmationOnce()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ExecutionDbContext(
            new DbContextOptionsBuilder<ExecutionDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var part = new PartSupply("Filtro", 10m, 5);
        db.PartSupplies.Add(part);
        await db.SaveChangesAsync();
        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var service = new ExecutionService(db);
        await service.ReserveAsync(new ReserveStockRequest(reservationId, orderId,
            [new CatalogItemRequest(part.Id, 2)]), CancellationToken.None);
        var command = new IntegrationEnvelope<ConsumeStockRequestedPayload>(
            Guid.NewGuid(), "ConsumeStockRequested", 1, orderId, Guid.NewGuid(),
            orderId, "sp-centro", DateTime.UtcNow,
            new ConsumeStockRequestedPayload(reservationId));
        var body = JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var handler = new ConsumeStockRequestedHandler(db, service);

        Assert.True(await handler.HandleAsync(body, CancellationToken.None));
        Assert.False(await handler.HandleAsync(body, CancellationToken.None));
        Assert.Equal(3, (await db.PartSupplies.SingleAsync()).StockQuantity);
        Assert.Equal(ReservationStatus.Consumed, (await db.StockReservations.SingleAsync()).Status);
        Assert.Equal("StockConsumed", (await db.OutboxMessages.SingleAsync()).EventType);
        Assert.Single(await db.InboxMessages.ToListAsync());
        await Assert.ThrowsAsync<OficinaMvp.Domain.Exceptions.DomainException>(() =>
            service.ReleaseAsync(reservationId, CancellationToken.None));
    }

    [Fact]
    public async Task ExecutionRequestRequiresConsumedReservationAndPublishesStarted()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ExecutionDbContext(
            new DbContextOptionsBuilder<ExecutionDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var paymentAttemptId = Guid.NewGuid();
        await new ExecutionService(db).ReserveAsync(new ReserveStockRequest(reservationId,
            orderId, []), CancellationToken.None);
        var request = new IntegrationEnvelope<ExecutionRequestedPayload>(Guid.NewGuid(),
            "ExecutionRequested", 1, orderId, Guid.NewGuid(), orderId, "sp-centro",
            DateTime.UtcNow, new ExecutionRequestedPayload(orderId, reservationId,
                paymentAttemptId));
        var body = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var handler = new ExecutionRequestedHandler(db);

        await Assert.ThrowsAsync<OficinaMvp.Domain.Exceptions.DomainException>(() =>
            handler.HandleAsync(body, CancellationToken.None));
        Assert.Empty(await db.InboxMessages.ToListAsync());
        await new ExecutionService(db).ConsumeAsync(reservationId, CancellationToken.None);
        Assert.True(await handler.HandleAsync(body, CancellationToken.None));
        Assert.False(await handler.HandleAsync(body, CancellationToken.None));
        var started = Assert.Single(await db.OutboxMessages.ToListAsync());
        Assert.Equal("ExecutionStarted", started.EventType);
        using var json = JsonDocument.Parse(started.Body);
        Assert.Equal(paymentAttemptId, json.RootElement.GetProperty("payload")
            .GetProperty("paymentAttemptId").GetGuid());
        Assert.Single(await db.InboxMessages.ToListAsync());
        var reservation = await db.StockReservations.SingleAsync();
        Assert.Equal(RepairStatus.Started, reservation.RepairStatus);
        Assert.Equal(paymentAttemptId, reservation.PaymentAttemptId);
        Assert.Equal("sp-centro", reservation.BranchId);
        var service = new ExecutionService(db);
        Assert.True(await service.FinishRepairAsync(reservationId, CancellationToken.None));
        Assert.False(await service.FinishRepairAsync(reservationId, CancellationToken.None));
        Assert.Equal(RepairStatus.Finished, reservation.RepairStatus);
        var finished = Assert.Single(await db.OutboxMessages
            .Where(item => item.EventType == "ExecutionFinished").ToListAsync());
        using var finishedJson = JsonDocument.Parse(finished.Body);
        Assert.Equal("sp-centro", finishedJson.RootElement.GetProperty("branchId").GetString());
        var lateDuplicate = request with { EventId = Guid.NewGuid() };
        Assert.False(await handler.HandleAsync(JsonSerializer.Serialize(lateDuplicate,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)), CancellationToken.None));
        Assert.Equal(2, await db.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task RejectedRepairPublishesOneEventAndCannotBeFinished()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ExecutionDbContext(
            new DbContextOptionsBuilder<ExecutionDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var service = new ExecutionService(db);
        await service.ReserveAsync(new ReserveStockRequest(reservationId, orderId, []),
            CancellationToken.None);
        await service.ConsumeAsync(reservationId, CancellationToken.None);
        var command = new IntegrationEnvelope<ExecutionRequestedPayload>(Guid.NewGuid(),
            "ExecutionRequested", 1, orderId, Guid.NewGuid(), orderId, "sp-centro",
            DateTime.UtcNow, new ExecutionRequestedPayload(orderId, reservationId, Guid.NewGuid()));
        await new ExecutionRequestedHandler(db).HandleAsync(JsonSerializer.Serialize(command,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)), CancellationToken.None);

        Assert.True(await service.RejectRepairAsync(reservationId, "Peça incompatível",
            CancellationToken.None));
        Assert.False(await service.RejectRepairAsync(reservationId, "Peça incompatível",
            CancellationToken.None));
        Assert.Equal(RepairStatus.Rejected, (await db.StockReservations.SingleAsync()).RepairStatus);
        Assert.Equal("ExecutionRejected", (await db.OutboxMessages
            .SingleAsync(item => item.EventType == "ExecutionRejected")).EventType);
        await Assert.ThrowsAsync<OficinaMvp.Domain.Exceptions.DomainException>(() =>
            service.FinishRepairAsync(reservationId, CancellationToken.None));
    }
}
