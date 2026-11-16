extern alias execution;

using System.Text.Json;
using Amazon.Runtime;
using Amazon.SQS;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OficinaMvp.Api.Messaging;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Infrastructure.Messaging;
using OficinaMvp.Infrastructure.Persistence;
using ExecutionDbContext = execution::Oficina.Execution.Persistence.ExecutionDbContext;
using ExecutionService = execution::Oficina.Execution.ExecutionService;
using ExecutionRequestedHandler = execution::Oficina.Execution.ExecutionRequestedHandler;
using ExecutionOutboxWorker = execution::Oficina.Execution.ExecutionOutboxWorker;
using ExecutionContracts = execution::Oficina.Execution.Contracts;
using Xunit;

namespace Oficina.Messaging.Local.Tests;

public sealed class ExecutionRejectionFlowTests
{
    [Fact]
    public async Task RejectedRepairCrossesQueueAndMovesOrderToManualReviewOnce()
    {
        var endpoint = Environment.GetEnvironmentVariable("MESSAGING_SQS_ENDPOINT")
            ?? throw new InvalidOperationException("Defina MESSAGING_SQS_ENDPOINT para o teste local.");
        using var sqs = new AmazonSQSClient(new BasicAWSCredentials("local", "local"),
            new AmazonSQSConfig { ServiceURL = endpoint });
        var queueUrl = (await sqs.CreateQueueAsync("os-rejection-" + Guid.NewGuid().ToString("N"))).QueueUrl;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:OsEventsQueueUrl"] = queueUrl
        }).Build();
        var osConnection = $"Data Source=os-rejection-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var executionConnection = $"Data Source=execution-rejection-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var osAnchor = new SqliteConnection(osConnection);
        await using var executionAnchor = new SqliteConnection(executionConnection);
        await osAnchor.OpenAsync();
        await executionAnchor.OpenAsync();
        var osOptions = new DbContextOptionsBuilder<WorkshopDbContext>().UseSqlite(osConnection).Options;
        var executionOptions = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseSqlite(executionConnection).Options;
        var customer = new Customer("Cliente Teste", "52998224725", null, null);
        var vehicle = new Vehicle(customer.Id, "BRA2E19", "VW", "Gol", 2020);
        var order = new WorkOrder(customer.Id, vehicle.Id,
            [new WorkOrderServiceLine(Guid.NewGuid(), "Revisão", 1, 125m)],
            [], null, DateTime.UtcNow, "sp-centro");
        var reservationId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        order.AcceptBudgetReady(Guid.NewGuid(), 1, DateTime.UtcNow);
        order.ApproveBudgetForPayment(DateTime.UtcNow, reservationId);
        order.ConfirmStockReservation(DateTime.UtcNow, paymentId);
        order.ConfirmPayment(DateTime.UtcNow);
        order.StartExecutionAfterPayment(DateTime.UtcNow);
        await using (var osSetup = new WorkshopDbContext(osOptions))
        {
            await osSetup.Database.EnsureCreatedAsync();
            osSetup.AddRange(customer, vehicle, order);
            await osSetup.SaveChangesAsync();
        }
        Guid rejectionEventId;
        await using (var executionSetup = new ExecutionDbContext(executionOptions))
        {
            await executionSetup.Database.EnsureCreatedAsync();
            var service = new ExecutionService(executionSetup);
            await service.ReserveAsync(new ExecutionContracts.ReserveStockRequest(
                reservationId, order.Id, []), CancellationToken.None);
            await service.ConsumeAsync(reservationId, CancellationToken.None);
            var request = new ExecutionContracts.IntegrationEnvelope<ExecutionContracts.ExecutionRequestedPayload>(
                Guid.NewGuid(), "ExecutionRequested", 1, order.Id, Guid.NewGuid(), order.Id,
                order.BranchId, DateTime.UtcNow,
                new ExecutionContracts.ExecutionRequestedPayload(order.Id, reservationId, paymentId));
            await new ExecutionRequestedHandler(executionSetup).HandleAsync(
                JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                CancellationToken.None);
            (await executionSetup.OutboxMessages.SingleAsync(item => item.EventType == "ExecutionStarted"))
                .MarkPublished(DateTime.UtcNow);
            Assert.True(await service.RejectRepairAsync(reservationId, "Peça incompatível",
                CancellationToken.None));
            Assert.False(await service.RejectRepairAsync(reservationId, "Peça incompatível",
                CancellationToken.None));
            await executionSetup.SaveChangesAsync();
            rejectionEventId = (await executionSetup.OutboxMessages
                .SingleAsync(item => item.EventType == "ExecutionRejected")).EventId;
        }
        using var executionServices = new ServiceCollection()
            .AddScoped(_ => new ExecutionDbContext(executionOptions))
            .BuildServiceProvider();
        using var osServices = new ServiceCollection()
            .AddScoped(_ => new WorkshopDbContext(osOptions))
            .AddScoped<ExecutionRejectedHandler>()
            .BuildServiceProvider();
        using var outboxWorker = new ExecutionOutboxWorker(sqs,
            executionServices.GetRequiredService<IServiceScopeFactory>(), configuration,
            NullLogger<ExecutionOutboxWorker>.Instance);
        using var osWorker = new OsEventsSqsWorker(sqs,
            osServices.GetRequiredService<IServiceScopeFactory>(), configuration,
            NullLogger<OsEventsSqsWorker>.Instance);
        await osWorker.StartAsync(CancellationToken.None);
        await outboxWorker.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(async () =>
            {
                await using var verify = new WorkshopDbContext(osOptions);
                return await verify.InboxMessages.AnyAsync(item => item.EventId == rejectionEventId);
            });
        }
        finally
        {
            await outboxWorker.StopAsync(CancellationToken.None);
            await osWorker.StopAsync(CancellationToken.None);
        }
        await using var finalOs = new WorkshopDbContext(osOptions);
        var finalOrder = await finalOs.WorkOrders.SingleAsync(item => item.Id == order.Id);
        Assert.Equal(WorkOrderProcessState.ManualReview, finalOrder.ProcessState);
        Assert.Equal(WorkOrderStatus.InExecution, finalOrder.Status);
        Assert.Null(finalOrder.FinalizedAtUtc);
        Assert.Single(await finalOs.InboxMessages.ToListAsync());
        await using var finalExecution = new ExecutionDbContext(executionOptions);
        var published = await finalExecution.OutboxMessages
            .SingleAsync(item => item.EventId == rejectionEventId);
        Assert.NotNull(published.PublishedAtUtc);
        Assert.False(await new ExecutionRejectedHandler(finalOs)
            .HandleAsync(published.Body, CancellationToken.None));
        Assert.Single(await finalOs.InboxMessages.ToListAsync());
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!await condition())
        {
            if (timeout.IsCancellationRequested)
                throw new TimeoutException("Recusa de execução não chegou à OS.");
            await Task.Delay(100);
        }
    }
}
