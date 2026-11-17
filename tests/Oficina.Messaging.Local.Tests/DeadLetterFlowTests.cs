extern alias execution;

using System.Text.Json;
using System.Diagnostics.Metrics;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Oficina.Billing;
using OficinaMvp.Api.Messaging;
using ExecutionCommandsWorker = execution::Oficina.Execution.ExecutionCommandsWorker;
using Xunit;

namespace Oficina.Messaging.Local.Tests;

public sealed class DeadLetterFlowTests
{
    [Theory]
    [InlineData("os")]
    [InlineData("billing")]
    [InlineData("execution")]
    public async Task InvalidMessageMovesToDeadLetterQueue(string consumer)
    {
        var meterName = consumer switch
        {
            "os" => "Oficina.WorkOrders",
            "billing" => "Oficina.Billing.Business",
            _ => "Oficina.Execution.Business"
        };
        long failures = 0;
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == meterName && instrument.Name == "sqs_message_failures")
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref failures, value));
        meterListener.Start();
        var endpoint = Environment.GetEnvironmentVariable("MESSAGING_SQS_ENDPOINT")
            ?? throw new InvalidOperationException("Defina MESSAGING_SQS_ENDPOINT para o teste local.");
        using var sqs = new AmazonSQSClient(new BasicAWSCredentials("local", "local"),
            new AmazonSQSConfig { ServiceURL = endpoint });
        var suffix = Guid.NewGuid().ToString("N");
        var deadLetterUrl = (await sqs.CreateQueueAsync($"{consumer}-dlq-{suffix}")).QueueUrl;
        var attributes = await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
        {
            QueueUrl = deadLetterUrl, AttributeNames = ["QueueArn"]
        });
        var deadLetterArn = attributes.Attributes["QueueArn"];
        var queueUrl = (await sqs.CreateQueueAsync(new CreateQueueRequest
        {
            QueueName = $"{consumer}-poison-{suffix}",
            Attributes = new Dictionary<string, string>
            {
                ["VisibilityTimeout"] = "1",
                ["RedrivePolicy"] = JsonSerializer.Serialize(new
                {
                    deadLetterTargetArn = deadLetterArn, maxReceiveCount = 2
                })
            }
        })).QueueUrl;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Messaging:OsEventsQueueUrl"] = queueUrl,
                ["Messaging:WorkOrdersQueueUrl"] = queueUrl,
                ["Messaging:CommandsQueueUrl"] = queueUrl
            }).Build();
        using var services = new ServiceCollection().BuildServiceProvider();
        var scopes = services.GetRequiredService<IServiceScopeFactory>();
        using BackgroundService worker = consumer switch
        {
            "os" => new OsEventsSqsWorker(sqs, scopes, configuration,
                NullLogger<OsEventsSqsWorker>.Instance),
            "billing" => new BillingSqsWorker(sqs, scopes, configuration,
                NullLogger<BillingSqsWorker>.Instance),
            _ => new ExecutionCommandsWorker(sqs, scopes, configuration,
                NullLogger<ExecutionCommandsWorker>.Instance)
        };
        await sqs.SendMessageAsync(queueUrl, "{\"eventType\":\"Unknown\"}");
        await worker.StartAsync(CancellationToken.None);
        Message? deadLetter = null;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
        {
            while (deadLetter is null && !timeout.IsCancellationRequested)
            {
                var received = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = deadLetterUrl, MaxNumberOfMessages = 1, WaitTimeSeconds = 1
                });
                deadLetter = received.Messages?.SingleOrDefault();
            }
        }
        await worker.StopAsync(CancellationToken.None);
        Assert.NotNull(deadLetter);
        Assert.Equal("{\"eventType\":\"Unknown\"}", deadLetter.Body);
        Assert.True(Volatile.Read(ref failures) >= 2);
    }
}
