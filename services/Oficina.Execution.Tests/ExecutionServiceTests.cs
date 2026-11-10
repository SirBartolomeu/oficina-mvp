using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Oficina.Execution;
using Oficina.Execution.Contracts;
using Oficina.Execution.Persistence;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Domain.Exceptions;
using Xunit;

namespace Oficina.Execution.Tests;

public sealed class ExecutionServiceTests
{
    [Fact]
    public async Task Quote_ShouldUseCatalogPricesAndAggregateRepeatedItems()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var db = CreateContext(connection);
        var service = new RepairService("Alinhamento", "Eixos", 100m, 30);
        var part = new PartSupply("Filtro", 12.50m, 5);
        db.AddRange(service, part);
        await db.SaveChangesAsync();

        var quote = await new ExecutionService(db).QuoteAsync(new QuoteRequest(
            [new(service.Id, 1)], [new(part.Id, 1), new(part.Id, 2)]), CancellationToken.None);

        Assert.Equal(13750, quote.TotalCents);
        Assert.Equal(3, Assert.Single(quote.Parts).Quantity);
        Assert.Equal(5, part.StockQuantity); // cotação não reserva
    }

    [Fact]
    public async Task ReserveAndRelease_ShouldBeIdempotentAndRestoreStockOnce()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var db = CreateContext(connection);
        var part = new PartSupply("Filtro", 12.50m, 5);
        db.PartSupplies.Add(part);
        await db.SaveChangesAsync();
        var service = new ExecutionService(db);
        var request = new ReserveStockRequest(Guid.NewGuid(), Guid.NewGuid(), [new(part.Id, 2)]);

        Assert.Equal("Reserved", (await service.ReserveAsync(request, CancellationToken.None)).Status);
        Assert.Equal("Reserved", (await service.ReserveAsync(request, CancellationToken.None)).Status);
        Assert.Equal(3, part.StockQuantity);
        Assert.Equal("Released", (await service.ReleaseAsync(request.ReservationId, CancellationToken.None))!.Status);
        Assert.Equal("Released", (await service.ReleaseAsync(request.ReservationId, CancellationToken.None))!.Status);
        Assert.Equal(5, part.StockQuantity);
        await Assert.ThrowsAsync<DomainException>(() => service.ReserveAsync(
            request with { WorkOrderId = Guid.NewGuid() }, CancellationToken.None));
    }

    [Fact]
    public async Task Reserve_ShouldRejectInsufficientStockWithoutCreatingReservation()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var db = CreateContext(connection);
        var part = new PartSupply("Filtro", 12.50m, 1);
        db.PartSupplies.Add(part);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() => new ExecutionService(db).ReserveAsync(
            new ReserveStockRequest(Guid.NewGuid(), Guid.NewGuid(), [new(part.Id, 2)]), CancellationToken.None));
        Assert.Equal(1, part.StockQuantity);
        Assert.Empty(db.StockReservations);
    }

    private static async Task<SqliteConnection> OpenDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        return connection;
    }

    private static ExecutionDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ExecutionDbContext>().UseSqlite(connection).Options);
}
