using System.Diagnostics;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OficinaMvp.Infrastructure.Messaging;
using OficinaMvp.Infrastructure.Persistence;
using Xunit;

namespace Oficina.Messaging.Local.Tests;

public sealed class SqsTraceContextTests
{
    [Fact]
    public void SqsActivitySourceEmitsChildSpanToListener()
    {
        Activity? stopped = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SqsTraceContext.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => stopped = activity
        };
        ActivitySource.AddActivityListener(listener);
        using var parent = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();

        using (var activity = SqsTraceContext.Start("SQS publish", ActivityKind.Producer, parent.Id))
        {
            Assert.Equal(parent.TraceId, activity.TraceId);
            Assert.Equal(parent.SpanId, activity.ParentSpanId);
            Assert.Equal(ActivityKind.Producer, activity.Kind);
            Assert.True(activity.IsAllDataRequested);
        }

        Assert.NotNull(stopped);
        Assert.Equal("SQS publish", stopped.OperationName);
    }

    [Fact]
    public async Task OutboxPublisherCarriesW3cParentThroughSqsAttribute()
    {
        var endpoint = Environment.GetEnvironmentVariable("MESSAGING_SQS_ENDPOINT")
            ?? throw new InvalidOperationException("Defina MESSAGING_SQS_ENDPOINT para o teste local.");
        using var sqs = new AmazonSQSClient(new BasicAWSCredentials("local", "local"),
            new AmazonSQSConfig { ServiceURL = endpoint });
        var queueUrl = (await sqs.CreateQueueAsync("trace-test-" + Guid.NewGuid().ToString("N"))).QueueUrl;
        try
        {
            using var parent = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
            var sender = new SqsIntegrationMessageSender(sqs, queueUrl);
            await sender.SendAsync(Guid.NewGuid(), "WorkOrderOpened", "{}", CancellationToken.None);

            var received = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = queueUrl, WaitTimeSeconds = 2,
                MessageAttributeNames = ["traceparent"]
            });
            var message = Assert.Single(received.Messages);
            var traceparent = message.MessageAttributes["traceparent"].StringValue;
            Assert.True(ActivityContext.TryParse(traceparent, null, out var context));
            Assert.Equal(parent.TraceId, context.TraceId);
            Assert.NotEqual(parent.SpanId, context.SpanId);
        }
        finally
        {
            await sqs.DeleteQueueAsync(queueUrl);
        }
    }

    [Fact]
    public async Task PersistedOutboxKeepsRequestTraceAcrossBackgroundDispatch()
    {
        var endpoint = Environment.GetEnvironmentVariable("MESSAGING_SQS_ENDPOINT")
            ?? throw new InvalidOperationException("Defina MESSAGING_SQS_ENDPOINT para o teste local.");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WorkshopDbContext>()
            .UseSqlite(connection).Options;
        await using var db = new WorkshopDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var eventId = Guid.NewGuid();
        ActivityTraceId requestTraceId;
        using (var request = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            requestTraceId = request.TraceId;
            db.OutboxMessages.Add(new OutboxMessage(eventId, "WorkOrderOpened", "{}", DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();
        var persisted = await db.OutboxMessages.SingleAsync(item => item.EventId == eventId);

        using var sqs = new AmazonSQSClient(new BasicAWSCredentials("local", "local"),
            new AmazonSQSConfig { ServiceURL = endpoint });
        var queueUrl = (await sqs.CreateQueueAsync("durable-trace-" + Guid.NewGuid().ToString("N"))).QueueUrl;
        try
        {
            var sender = new SqsIntegrationMessageSender(sqs, queueUrl);
            await sender.SendAsync(eventId, "WorkOrderOpened", persisted.PayloadJson, CancellationToken.None);
            var received = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = queueUrl, WaitTimeSeconds = 2,
                MessageAttributeNames = ["traceparent"]
            });
            var message = Assert.Single(received.Messages);
            Assert.True(ActivityContext.TryParse(
                message.MessageAttributes["traceparent"].StringValue, null, out var context));
            Assert.Equal(requestTraceId, context.TraceId);
        }
        finally
        {
            await sqs.DeleteQueueAsync(queueUrl);
        }
    }
}
