using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Infrastructure.Messaging;
using OficinaMvp.Infrastructure.Persistence;

namespace OficinaMvp.Integration.Tests;

public sealed class PaymentCompensationTests
{
    [Theory]
    [InlineData("expired")]
    [InlineData("canceled")]
    [InlineData("failed")]
    public async Task RejectionRequestsReleaseAndCancelsOnlyAfterConfirmation(string reason)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WorkshopDbContext>().UseSqlite(connection).Options;
        await using var db = new WorkshopDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var customer = new Customer("Cliente Teste", "52998224725", null, null);
        var vehicle = new Vehicle(customer.Id, "BRA2E19", "VW", "Gol", 2020);
        var order = new WorkOrder(customer.Id, vehicle.Id,
            [new WorkOrderServiceLine(Guid.NewGuid(), "Revisão", 1, 125m)],
            [], null, DateTime.UtcNow, "sp-centro");
        var budgetId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        order.AcceptBudgetReady(budgetId, 1, DateTime.UtcNow);
        order.ApproveBudgetForPayment(DateTime.UtcNow, reservationId);
        order.ConfirmStockReservation(DateTime.UtcNow, paymentId);
        db.AddRange(customer, vehicle, order);
        await db.SaveChangesAsync();
        var rejected = new IntegrationEnvelope<PaymentRejectedPayload>(Guid.NewGuid(),
            "PaymentRejected", 1, order.Id, Guid.NewGuid(), order.Id, order.BranchId,
            DateTime.UtcNow, new PaymentRejectedPayload(paymentId, "ORDTEST123", 12500,
                "BRL", reason));
        var rejectedBody = JsonSerializer.Serialize(rejected,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var rejectionHandler = new PaymentRejectedHandler(db);

        Assert.True(await rejectionHandler.HandleAsync(rejectedBody, CancellationToken.None));
        Assert.False(await rejectionHandler.HandleAsync(rejectedBody, CancellationToken.None));
        Assert.Equal(WorkOrderProcessState.ReleasingStock, order.ProcessState);
        var release = Assert.Single(await db.OutboxMessages.ToListAsync());
        Assert.Equal("ReleaseStockRequested", release.EventType);
        using (var json = JsonDocument.Parse(release.PayloadJson))
            Assert.Equal(reservationId,
                json.RootElement.GetProperty("payload").GetProperty("reservationId").GetGuid());

        var released = new IntegrationEnvelope<StockReleasedPayload>(Guid.NewGuid(),
            "StockReleased", 1, order.Id, release.EventId, order.Id, order.BranchId,
            DateTime.UtcNow, new StockReleasedPayload(reservationId));
        var releasedBody = JsonSerializer.Serialize(released,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var releaseHandler = new StockReleasedHandler(db);
        Assert.True(await releaseHandler.HandleAsync(releasedBody, CancellationToken.None));
        Assert.False(await releaseHandler.HandleAsync(releasedBody, CancellationToken.None));
        Assert.Equal(WorkOrderProcessState.Canceled, order.ProcessState);
        Assert.Equal(2, await db.InboxMessages.CountAsync());
    }
}
