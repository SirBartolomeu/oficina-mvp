using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Oficina.Billing;

// Cada entrega tem uma chave própria: uma atualização nova nunca é apagada pelo
// consumidor de uma entrega anterior. A reconciliação/outbox é idempotente.
public sealed class OrderNotificationQueue(IAmazonDynamoDB dynamo, IConfiguration configuration)
{
    private readonly string table = configuration["DynamoDb:Table"]!;
    public async Task EnqueueAsync(string orderId, CancellationToken ct)
    {
        await dynamo.PutItemAsync(new PutItemRequest
        {
            TableName = table,
            Item = new()
            {
                ["pk"] = new() { S = "ORDER#NOTIFICATIONS" },
                ["sk"] = new() { S = $"{DateTime.UtcNow:O}#{Guid.NewGuid():N}" },
                ["orderId"] = new() { S = orderId }
            }
        }, ct);
    }

    public async Task<int> ProcessAsync(PaymentReconciliationService reconciliation,
        ILogger logger, CancellationToken ct)
    {
        var response = await dynamo.QueryAsync(new QueryRequest
        {
            TableName = table, KeyConditionExpression = "pk = :pk",
            ExpressionAttributeValues = new() { [":pk"] = new() { S = "ORDER#NOTIFICATIONS" } },
            ConsistentRead = true, Limit = 25
        }, ct);
        var completed = 0;
        foreach (var item in response.Items ?? [])
        {
            try
            {
                await reconciliation.ReconcileAsync(item["orderId"].S, ct);
                await dynamo.DeleteItemAsync(table, new Dictionary<string, AttributeValue>
                {
                    ["pk"] = item["pk"], ["sk"] = item["sk"]
                }, ct);
                completed++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                logger.LogWarning(error, "Notificação de order pendente de reconciliação");
                // Reagendar no final da fila sem perder a entrega, evitando bloquear as seguintes.
                await EnqueueAsync(item["orderId"].S, ct);
                await dynamo.DeleteItemAsync(table, new Dictionary<string, AttributeValue>
                {
                    ["pk"] = item["pk"], ["sk"] = item["sk"]
                }, ct);
            }
        }
        return completed;
    }
}

public sealed class OrderNotificationWorker(IServiceScopeFactory scopes,
    ILogger<OrderNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<OrderNotificationQueue>().ProcessAsync(
                    scope.ServiceProvider.GetRequiredService<PaymentReconciliationService>(), logger, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogError(error, "Falha na fila de notificações"); }
        }
    }
}
