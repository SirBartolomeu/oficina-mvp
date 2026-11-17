using System.Diagnostics;
using Amazon.SQS;
using Amazon.SQS.Model;
using System.Text.Json;

namespace Oficina.Billing;

public sealed class BillingSqsWorker(
    IAmazonSQS sqs,
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<BillingSqsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueUrl = configuration["Messaging:WorkOrdersQueueUrl"]
            ?? throw new InvalidOperationException("Messaging:WorkOrdersQueueUrl é obrigatório.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = queueUrl,
                    MaxNumberOfMessages = 10,
                    WaitTimeSeconds = 10,
                    MessageAttributeNames = ["traceparent"]
                }, stoppingToken);
                foreach (var message in response.Messages ?? [])
                {
                    var started = Stopwatch.GetTimestamp();
                    string? eventId = null;
                    string? correlationId = null;
                    try
                    {
                        using var scope = scopes.CreateScope();
                        using var envelope = JsonDocument.Parse(message.Body);
                        var root = envelope.RootElement;
                        var eventType = root.GetProperty("eventType").GetString();
                        eventId = root.GetProperty("eventId").GetString();
                        correlationId = root.GetProperty("correlationId").GetString();
                        var parent = message.MessageAttributes?.TryGetValue("traceparent", out var traceAttribute) == true
                            ? traceAttribute.StringValue : null;
                        using var activity = SqsTraceContext.Start("SQS consume", ActivityKind.Consumer, parent);
                        activity.SetTag("messaging.system", "aws_sqs");
                        activity.SetTag("messaging.operation.name", "process");
                        activity.SetTag("messaging.message.type", eventType);
                        using var logScope = logger.BeginScope(new Dictionary<string, object?>
                        {
                            ["eventId"] = eventId,
                            ["correlationId"] = correlationId,
                            ["workOrderId"] = root.GetProperty("workOrderId").GetString()
                        });
                        if (eventType == "WorkOrderOpened")
                            await scope.ServiceProvider.GetRequiredService<WorkOrderOpenedHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                        else if (eventType == "CreatePaymentRequested")
                        {
                            var accepted = await scope.ServiceProvider.GetRequiredService<PaymentRequestHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                            if (accepted) BillingMetrics.RecordAcceptedCommand(eventType);
                        }
                        else if (eventType == "RefundPaymentRequested")
                        {
                            var accepted = await scope.ServiceProvider.GetRequiredService<RefundRequestHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                            if (accepted) BillingMetrics.RecordAcceptedCommand(eventType);
                        }
                        else
                            throw new InvalidOperationException($"Comando não suportado: {eventType}");
                        await sqs.DeleteMessageAsync(queueUrl, message.ReceiptHandle, stoppingToken);
                        BillingMetrics.RecordSqsProcessed(eventType, Stopwatch.GetElapsedTime(started).TotalSeconds);
                        logger.LogInformation("Comando {EventType} processado no Billing", eventType);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception error)
                    {
                        BillingMetrics.RecordSqsMessageFailure();
                        logger.LogWarning(error,
                            "Falha ao consumir mensagem {MessageId} {EventId} {CorrelationId} no Billing",
                            message.MessageId, eventId, correlationId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                BillingMetrics.RecordSqsReceiveFailure();
                logger.LogError(error, "Falha ao receber mensagens SQS no Billing");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
