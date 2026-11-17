using System.Diagnostics;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace Oficina.Billing;

public sealed class BillingOutboxWorker(IAmazonSQS sqs, IServiceScopeFactory scopes,
    IConfiguration configuration, ILogger<BillingOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueUrl = configuration["Messaging:OsEventsQueueUrl"]
            ?? throw new InvalidOperationException("Messaging:OsEventsQueueUrl é obrigatório.");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<BillingRepository>();
                var dispatcher = new BillingOutboxDispatcher(repository, async (item, ct) =>
                {
                    var storedParent = SqsTraceContext.Read(item.Body);
                    using var activity = SqsTraceContext.Start("SQS publish", ActivityKind.Producer, storedParent);
                    activity.SetTag("messaging.system", "aws_sqs");
                    activity.SetTag("messaging.operation.name", "publish");
                    var request = new SendMessageRequest
                    {
                        QueueUrl = queueUrl, MessageBody = item.Body,
                        MessageAttributes = new Dictionary<string, MessageAttributeValue>
                        {
                            ["traceparent"] = new() { DataType = "String", StringValue = activity.Id }
                        }
                    };
                    if (queueUrl.EndsWith(".fifo", StringComparison.OrdinalIgnoreCase))
                    {
                        using var body = JsonDocument.Parse(item.Body);
                        request.MessageGroupId = body.RootElement.GetProperty("workOrderId").GetString();
                        request.MessageDeduplicationId = item.EventId.ToString("N");
                    }
                    await sqs.SendMessageAsync(request, ct);
                }, logger);
                await dispatcher.DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Falha ao processar Outbox do Billing");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
