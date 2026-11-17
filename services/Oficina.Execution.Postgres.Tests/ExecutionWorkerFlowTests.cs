using System.Text.Json;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Oficina.Execution.Contracts;
using Oficina.Execution.Persistence;
using Xunit;

namespace Oficina.Execution.Postgres.Tests;

public sealed class ExecutionWorkerFlowTests
{
    [Fact]
    public async Task ReserveCommandCrossesSqsInboxAndOutboxOnce()
    {
        var connectionString = Environment.GetEnvironmentVariable("EXECUTION_WORKER_POSTGRES_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Defina EXECUTION_WORKER_POSTGRES_CONNECTION_STRING.");
        var endpoint = Environment.GetEnvironmentVariable("MESSAGING_SQS_ENDPOINT")
            ?? throw new InvalidOperationException("Defina MESSAGING_SQS_ENDPOINT.");
        using var sqs = new AmazonSQSClient(new BasicAWSCredentials("local", "local"),
            new AmazonSQSConfig { ServiceURL = endpoint });
        var commands = (await sqs.CreateQueueAsync("execution-bdd-commands-" + Guid.NewGuid().ToString("N"))).QueueUrl;
        var events = (await sqs.CreateQueueAsync("execution-bdd-events-" + Guid.NewGuid().ToString("N"))).QueueUrl;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:CommandsQueueUrl"] = commands,
            ["Messaging:OsEventsQueueUrl"] = events
        }).Build();
        var options = new DbContextOptionsBuilder<ExecutionDbContext>().UseNpgsql(connectionString).Options;
        var eventId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var command = new IntegrationEnvelope<ReserveStockRequestedPayload>(eventId,
            "ReserveStockRequested", 1, orderId, Guid.NewGuid(), orderId, "sp-centro",
            DateTime.UtcNow, new ReserveStockRequestedPayload(reservationId, 1, [],
                DateTime.UtcNow.AddMinutes(5)));
        var body = JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await using (var setup = new ExecutionDbContext(options))
            await setup.Database.MigrateAsync();
        using var services = new ServiceCollection()
            .AddScoped(_ => new ExecutionDbContext(options))
            .AddScoped<ExecutionService>()
            .AddScoped<ReserveStockRequestedHandler>()
            .BuildServiceProvider();
        using var commandWorker = new ExecutionCommandsWorker(sqs,
            services.GetRequiredService<IServiceScopeFactory>(), configuration,
            NullLogger<ExecutionCommandsWorker>.Instance);
        using var outboxWorker = new ExecutionOutboxWorker(sqs,
            services.GetRequiredService<IServiceScopeFactory>(), configuration,
            NullLogger<ExecutionOutboxWorker>.Instance);
        await commandWorker.StartAsync(CancellationToken.None);
        await outboxWorker.StartAsync(CancellationToken.None);
        try
        {
            await sqs.SendMessageAsync(commands, body);
            await WaitUntilAsync(async () =>
            {
                await using var verify = new ExecutionDbContext(options);
                return await verify.InboxMessages.AnyAsync(item => item.EventId == eventId) &&
                       await verify.OutboxMessages.AnyAsync(item => item.EventType == "StockReserved" &&
                           item.PublishedAtUtc != null && item.Body.Contains(reservationId.ToString()));
            });
            var received = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = events, MaxNumberOfMessages = 1, WaitTimeSeconds = 1
            });
            var message = Assert.Single(received.Messages);
            using var result = JsonDocument.Parse(message.Body);
            Assert.Equal("StockReserved", result.RootElement.GetProperty("eventType").GetString());
            Assert.Equal(reservationId, result.RootElement.GetProperty("payload")
                .GetProperty("reservationId").GetGuid());

            await using var final = new ExecutionDbContext(options);
            Assert.Single(await final.InboxMessages.Where(item => item.EventId == eventId).ToListAsync());
            Assert.Single(await final.OutboxMessages.Where(item => item.EventType == "StockReserved" &&
                item.Body.Contains(reservationId.ToString())).ToListAsync());
        }
        finally
        {
            await outboxWorker.StopAsync(CancellationToken.None);
            await commandWorker.StopAsync(CancellationToken.None);
            await using var cleanup = new ExecutionDbContext(options);
            await cleanup.OutboxMessages.Where(item => item.Body.Contains(reservationId.ToString()))
                .ExecuteDeleteAsync();
            await cleanup.InboxMessages.Where(item => item.EventId == eventId).ExecuteDeleteAsync();
            await cleanup.StockReservations.Where(item => item.Id == reservationId).ExecuteDeleteAsync();
            await sqs.DeleteQueueAsync(commands);
            await sqs.DeleteQueueAsync(events);
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!await condition())
        {
            if (timeout.IsCancellationRequested) throw new TimeoutException("Worker não concluiu o fluxo SQS.");
            await Task.Delay(100);
        }
    }
}
