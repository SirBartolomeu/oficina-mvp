using Amazon.SQS;
using Amazon.SQS.Model;

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
                    WaitTimeSeconds = 10
                }, stoppingToken);
                foreach (var message in response.Messages ?? [])
                {
                    try
                    {
                        using var scope = scopes.CreateScope();
                        await scope.ServiceProvider.GetRequiredService<WorkOrderOpenedHandler>()
                            .HandleAsync(message.Body, stoppingToken);
                        await sqs.DeleteMessageAsync(queueUrl, message.ReceiptHandle, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception error)
                    {
                        logger.LogWarning(error, "Falha ao consumir mensagem {MessageId} no Billing", message.MessageId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Falha ao receber mensagens SQS no Billing");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
