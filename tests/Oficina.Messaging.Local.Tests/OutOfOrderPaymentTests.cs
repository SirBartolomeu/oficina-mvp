using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Domain.Exceptions;
using OficinaMvp.Infrastructure.Messaging;
using OficinaMvp.Infrastructure.Persistence;
using Xunit;

namespace Oficina.Messaging.Local.Tests;

public sealed class OutOfOrderPaymentTests
{
    [Fact]
    public async Task PaymentConfirmationRetriesAfterStockReservationArrives()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WorkshopDbContext>()
            .UseSqlite(connection).Options;
        var customer = new Customer("Cliente Teste", "52998224725", null, null);
        var vehicle = new Vehicle(customer.Id, "BRA2E19", "VW", "Gol", 2020);
        var order = new WorkOrder(customer.Id, vehicle.Id,
            [new WorkOrderServiceLine(Guid.NewGuid(), "Revisão", 1, 125m)],
            [], null, DateTime.UtcNow, "sp-centro");
        order.AcceptBudgetReady(Guid.NewGuid(), 1, DateTime.UtcNow);
        order.ApproveBudgetForPayment(DateTime.UtcNow, Guid.NewGuid());
        var paymentId = Guid.NewGuid();
        var envelope = new IntegrationEnvelope<PaymentConfirmedPayload>(
            Guid.NewGuid(), "PaymentConfirmed", 1, order.Id, Guid.NewGuid(),
            order.Id, order.BranchId, DateTime.UtcNow,
            new PaymentConfirmedPayload(paymentId, "ORDTEST123", 12500, "BRL"));
        var body = JsonSerializer.Serialize(envelope,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await using (var setup = new WorkshopDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.AddRange(customer, vehicle, order);
            await setup.SaveChangesAsync();
        }

        await using (var early = new WorkshopDbContext(options))
        {
            await Assert.ThrowsAsync<DomainException>(() =>
                new PaymentConfirmedHandler(early).HandleAsync(body, CancellationToken.None));
            Assert.Equal(WorkOrderProcessState.AwaitingStockReservation,
                (await early.WorkOrders.SingleAsync()).ProcessState);
            Assert.Empty(await early.InboxMessages.ToListAsync());
            Assert.Empty(await early.OutboxMessages.ToListAsync());
        }

        await using (var reserved = new WorkshopDbContext(options))
        {
            var current = await reserved.WorkOrders.SingleAsync();
            current.ConfirmStockReservation(DateTime.UtcNow, paymentId);
            await reserved.SaveChangesAsync();
        }
        await using (var retried = new WorkshopDbContext(options))
        {
            var handler = new PaymentConfirmedHandler(retried);
            Assert.True(await handler.HandleAsync(body, CancellationToken.None));
            Assert.False(await handler.HandleAsync(body, CancellationToken.None));
            Assert.Equal(WorkOrderProcessState.ReadyForExecution,
                (await retried.WorkOrders.SingleAsync()).ProcessState);
            Assert.Single(await retried.InboxMessages.ToListAsync());
            Assert.Equal("ConsumeStockRequested",
                (await retried.OutboxMessages.SingleAsync()).EventType);
        }
    }
}
