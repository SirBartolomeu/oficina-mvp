using System.Text.Json;
using Oficina.Billing;
using Xunit;

namespace Oficina.Billing.Tests;

public sealed class WorkOrderOpenedHandlerTests
{
    [Fact]
    public async Task ValidEventCreatesBudgetUsingQuotedCents()
    {
        var store = new RecordingStore();
        var handler = new WorkOrderOpenedHandler(store);
        var body = Serialize(12500);

        var budget = await handler.HandleAsync(body, CancellationToken.None);

        Assert.Equal(12500, budget.TotalCents);
        Assert.Equal(1, budget.Version);
        Assert.Equal("sp-centro", budget.BranchId);
        Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task TamperedTotalCannotReachDynamoDb()
    {
        var store = new RecordingStore();
        var handler = new WorkOrderOpenedHandler(store);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.HandleAsync(Serialize(9999), CancellationToken.None));

        Assert.Equal(0, store.Calls);
    }

    private static string Serialize(long total) => JsonSerializer.Serialize(
        new WorkOrderOpenedEnvelope(Guid.NewGuid(), "WorkOrderOpened", 1, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "sp-centro", DateTime.UtcNow,
            new WorkOrderOpenedPayload(1,
                [new WorkOrderOpenedLine("Service", Guid.NewGuid(), "Revisão", 1, 12500)],
                total, "BRL", DateTime.UtcNow)),
        new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private sealed class RecordingStore : IBudgetEventStore
    {
        public int Calls { get; private set; }
        public Task<Budget> CreateFromEventAsync(Guid eventId, Budget proposed,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(proposed);
        }
    }
}
