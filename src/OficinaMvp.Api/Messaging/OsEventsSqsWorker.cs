using System.Diagnostics;
using Amazon.SQS;
using Amazon.SQS.Model;
using System.Text.Json;
using OficinaMvp.Application.Observability;
using OficinaMvp.Infrastructure.Messaging;

namespace OficinaMvp.Api.Messaging;

public sealed class OsEventsSqsWorker(IAmazonSQS sqs, IServiceScopeFactory scopes,
    IConfiguration configuration, ILogger<OsEventsSqsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueUrl = configuration["Messaging:OsEventsQueueUrl"]
            ?? throw new InvalidOperationException("Messaging:OsEventsQueueUrl é obrigatório.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = queueUrl, MaxNumberOfMessages = 10, WaitTimeSeconds = 10,
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
                        bool changed;
                        if (eventType == "BudgetReady")
                            changed = await scope.ServiceProvider.GetRequiredService<BudgetReadyHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                        else if (eventType is "BudgetApproved" or "BudgetRefused")
                            changed = await scope.ServiceProvider.GetRequiredService<BudgetDecisionHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                        else if (eventType is "StockReserved" or "StockReservationRejected")
                            changed = await scope.ServiceProvider.GetRequiredService<StockReservationResultHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                        else if (eventType == "PaymentConfirmed")
                            changed = await scope.ServiceProvider.GetRequiredService<PaymentConfirmedHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                        else if (eventType == "PaymentRejected")
                            changed = await scope.ServiceProvider.GetRequiredService<PaymentRejectedHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                        else if (eventType == "StockReleased")
                            changed = await scope.ServiceProvider.GetRequiredService<StockReleasedHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                        else if (eventType == "StockConsumed")
                            changed = await scope.ServiceProvider.GetRequiredService<StockConsumedHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                        else if (eventType == "ExecutionStarted")
                            changed = await scope.ServiceProvider.GetRequiredService<ExecutionStartedHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                        else if (eventType == "ExecutionFinished")
                            changed = await scope.ServiceProvider.GetRequiredService<ExecutionFinishedHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                        else if (eventType == "ExecutionRejected")
                            changed = await scope.ServiceProvider.GetRequiredService<ExecutionRejectedHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                        else if (eventType == "RefundConfirmed")
                            changed = await scope.ServiceProvider.GetRequiredService<RefundConfirmedHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                        else
                            throw new InvalidOperationException($"Evento não suportado: {eventType}");
                        if (changed) WorkOrderMetrics.RecordSagaTransition(eventType);
                        await sqs.DeleteMessageAsync(queueUrl, message.ReceiptHandle, stoppingToken);
                        WorkOrderMetrics.RecordSqsProcessed(eventType, Stopwatch.GetElapsedTime(started).TotalSeconds);
                        logger.LogInformation("Evento {EventType} processado na OS", eventType);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception error)
                    {
                        WorkOrderMetrics.RecordSqsMessageFailure();
                        logger.LogWarning(error,
                            "Falha ao consumir evento {MessageId} {EventId} {CorrelationId} na OS",
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
                WorkOrderMetrics.RecordSqsReceiveFailure();
                logger.LogError(error, "Falha ao receber eventos SQS na OS");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
