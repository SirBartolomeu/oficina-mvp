using System.Diagnostics.Metrics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OficinaMvp.Application.Observability;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Infrastructure.Persistence;

namespace OficinaMvp.Integration.Tests;

public sealed class WorkOrderStateDurationTests
{
    [Fact]
    public async Task SavingStateChangeRecordsElapsedTimeOnce()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WorkshopDbContext>()
            .UseSqlite(connection).Options;
        await using var db = new WorkshopDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var startedAt = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
        var customer = new Customer("Cliente teste", "52998224725", null, null);
        var vehicle = new Vehicle(customer.Id, "BRA2E19", "Marca", "Modelo", 2020);
        var order = new WorkOrder(customer.Id, vehicle.Id,
            [new WorkOrderServiceLine(Guid.NewGuid(), "Serviço", 1, 100m)],
            [], null, startedAt);
        db.AddRange(customer, vehicle, order);

        var observations = new List<(string State, double Seconds)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == WorkOrderMetrics.MeterName &&
                instrument.Name == "work_order_process_state_duration")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            var state = "";
            foreach (var tag in tags)
                if (tag.Key == "state") state = tag.Value?.ToString() ?? "";
            observations.Add((state, value));
        });
        listener.Start();

        await db.SaveChangesAsync();
        Assert.Empty(observations);

        order.SendBudget(startedAt.AddMinutes(2));
        await db.SaveChangesAsync();
        await db.SaveChangesAsync();

        Assert.Single(observations);
        Assert.Equal(("AwaitingBudget", 120d), observations[0]);
        Assert.Equal(startedAt.AddMinutes(2), order.ProcessStateEnteredAtUtc);

        db.ChangeTracker.Clear();
        var reloaded = await db.WorkOrders.Include(item => item.StatusHistory)
            .SingleAsync(item => item.Id == order.Id);
        reloaded.ApproveBudgetForPayment(startedAt.AddMinutes(5));
        await db.SaveChangesAsync();

        Assert.Equal(2, observations.Count);
        Assert.Equal(("AwaitingApproval", 180d), observations[1]);
    }
}
