using Microsoft.EntityFrameworkCore;
using Oficina.Execution;
using Oficina.Execution.Contracts;
using Oficina.Execution.Persistence;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Domain.Exceptions;
using Xunit;

namespace Oficina.Execution.Postgres.Tests;

public sealed class StockConcurrencyTests
{
    [Fact]
    public async Task CompetingReservationRequestsCannotOversellOnePart()
    {
        var connectionString = Environment.GetEnvironmentVariable("EXECUTION_POSTGRES_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Defina EXECUTION_POSTGRES_CONNECTION_STRING para executar este teste PostgreSQL.");
        var options = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseNpgsql(connectionString).Options;
        var part = new PartSupply("Reserva " + Guid.NewGuid(), 10m, 1);
        var reservationIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await using (var setup = new ExecutionDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.PartSupplies.Add(part);
            await setup.SaveChangesAsync();
        }

        try
        {
            async Task<Exception?> ReserveAsync(Guid reservationId)
            {
                await using var context = new ExecutionDbContext(options);
                try
                {
                    await new ExecutionService(context).ReserveAsync(
                        new ReserveStockRequest(reservationId, Guid.NewGuid(), [new(part.Id, 1)]),
                        CancellationToken.None);
                    return null;
                }
                catch (Exception error) when (error is DomainException or DbUpdateConcurrencyException)
                {
                    return error;
                }
            }

            var results = await Task.WhenAll(reservationIds.Select(ReserveAsync));
            Assert.Single(results, result => result is null);
            Assert.Single(results, result => result is not null);
            await using var verify = new ExecutionDbContext(options);
            Assert.Equal(0, (await verify.PartSupplies.SingleAsync(item => item.Id == part.Id)).StockQuantity);
            Assert.Equal(1, await verify.StockReservations.CountAsync(item => reservationIds.Contains(item.Id)));
        }
        finally
        {
            await CleanupAsync(options, part.Id, reservationIds);
        }
    }

    [Fact]
    public async Task TwoReservationsLoadedFromSameStockVersionCannotBothCommit()
    {
        var connectionString = Environment.GetEnvironmentVariable("EXECUTION_POSTGRES_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Defina EXECUTION_POSTGRES_CONNECTION_STRING para executar este teste PostgreSQL.");
        var options = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseNpgsql(connectionString).Options;
        var part = new PartSupply("Concorrência " + Guid.NewGuid(), 10m, 1);
        var firstReservationId = Guid.NewGuid();
        var secondReservationId = Guid.NewGuid();

        await using (var setup = new ExecutionDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.PartSupplies.Add(part);
            await setup.SaveChangesAsync();
        }

        try
        {
            await using var first = new ExecutionDbContext(options);
            await using var second = new ExecutionDbContext(options);
            var firstPart = await first.PartSupplies.SingleAsync(item => item.Id == part.Id);
            var secondPart = await second.PartSupplies.SingleAsync(item => item.Id == part.Id);
            firstPart.RemoveFromStock(1);
            secondPart.RemoveFromStock(1);
            first.StockReservations.Add(new StockReservation(firstReservationId, Guid.NewGuid(),
                [new StockReservationLine(part.Id, 1)]));
            second.StockReservations.Add(new StockReservation(secondReservationId, Guid.NewGuid(),
                [new StockReservationLine(part.Id, 1)]));

            await first.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

            await using var verify = new ExecutionDbContext(options);
            Assert.Equal(0, (await verify.PartSupplies.SingleAsync(item => item.Id == part.Id)).StockQuantity);
            Assert.Equal(1, await verify.StockReservations.CountAsync(item =>
                item.Id == firstReservationId || item.Id == secondReservationId));
        }
        finally
        {
            await CleanupAsync(options, part.Id, [firstReservationId, secondReservationId]);
        }
    }

    private static async Task CleanupAsync(DbContextOptions<ExecutionDbContext> options, Guid partId,
        Guid[] reservationIds)
    {
        await using var cleanup = new ExecutionDbContext(options);
        await cleanup.Set<StockReservationLine>().Where(item => item.PartId == partId).ExecuteDeleteAsync();
        await cleanup.StockReservations.Where(item => reservationIds.Contains(item.Id)).ExecuteDeleteAsync();
        await cleanup.PartSupplies.Where(item => item.Id == partId).ExecuteDeleteAsync();
    }
}
