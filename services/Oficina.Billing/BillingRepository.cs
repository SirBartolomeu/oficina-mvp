using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System.Text.Json;

namespace Oficina.Billing;

public sealed class BillingRepository(IAmazonDynamoDB dynamo, IConfiguration configuration) : IBudgetEventStore
{
    private readonly string _table = configuration["DynamoDb:Table"]
        ?? throw new InvalidOperationException("DynamoDb:Table é obrigatório.");

    public async Task<Budget> CreateAsync(Budget proposed, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(proposed.WorkOrderId, proposed.Version, cancellationToken);
        if (existing is not null)
        {
            if (existing.TotalCents == proposed.TotalCents &&
                existing.BranchId == proposed.BranchId &&
                JsonSerializer.Serialize(existing.Lines) == JsonSerializer.Serialize(proposed.Lines))
                return existing;
            throw new InvalidOperationException("Versão do orçamento já existe com conteúdo diferente.");
        }

        try
        {
            await dynamo.PutItemAsync(new PutItemRequest
            {
                TableName = _table,
                Item = ToItem(proposed),
                ConditionExpression = "attribute_not_exists(pk)"
            }, cancellationToken);
            return proposed;
        }
        catch (ConditionalCheckFailedException)
        {
            var current = await GetAsync(proposed.WorkOrderId, proposed.Version, cancellationToken);
            if (current is not null && current.TotalCents == proposed.TotalCents &&
                current.BranchId == proposed.BranchId &&
                JsonSerializer.Serialize(current.Lines) == JsonSerializer.Serialize(proposed.Lines))
                return current;
            throw new InvalidOperationException("Conflito ao criar orçamento.");
        }
    }

    public async Task<Budget> CreateFromEventAsync(Guid eventId, Budget proposed,
        CancellationToken cancellationToken)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("Identificador do evento obrigatório.");
        var inboxItem = InboxKey(eventId);
        inboxItem["receivedAtUtc"] = new AttributeValue { S = DateTime.UtcNow.ToString("O") };
        try
        {
            await dynamo.TransactWriteItemsAsync(new TransactWriteItemsRequest
            {
                TransactItems =
                [
                    new TransactWriteItem { Put = new Put
                    {
                        TableName = _table, Item = ToItem(proposed),
                        ConditionExpression = "attribute_not_exists(pk)"
                    } },
                    new TransactWriteItem { Put = new Put
                    {
                        TableName = _table, Item = inboxItem,
                        ConditionExpression = "attribute_not_exists(pk)"
                    } }
                ]
            }, cancellationToken);
            return proposed;
        }
        catch (TransactionCanceledException)
        {
            var inbox = await dynamo.GetItemAsync(new GetItemRequest
            {
                TableName = _table, Key = InboxKey(eventId), ConsistentRead = true
            }, cancellationToken);
            if (inbox.Item is not { Count: > 0 })
                throw new InvalidOperationException("Conflito ao gravar orçamento e Inbox.");
            var current = await GetAsync(proposed.WorkOrderId, proposed.Version, cancellationToken);
            if (current is null || current.TotalCents != proposed.TotalCents ||
                current.BranchId != proposed.BranchId ||
                JsonSerializer.Serialize(current.Lines) != JsonSerializer.Serialize(proposed.Lines))
                throw new InvalidOperationException("Evento já processado com orçamento diferente.");
            return current;
        }
    }

    public async Task<Budget?> GetAsync(Guid workOrderId, int version, CancellationToken cancellationToken)
    {
        var response = await dynamo.GetItemAsync(new GetItemRequest
        {
            TableName = _table,
            Key = Key(workOrderId, version),
            ConsistentRead = true
        }, cancellationToken);
        return response.Item is { Count: > 0 } ? FromItem(response.Item) : null;
    }

    public async Task<Budget?> DecideAsync(Guid workOrderId, int version, bool approved, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(workOrderId, version, cancellationToken);
        if (existing is null) return null;
        if (existing.Status != BudgetStatus.AwaitingApproval)
        {
            if (existing.Status == (approved ? BudgetStatus.Approved : BudgetStatus.Refused)) return existing;
            throw new InvalidOperationException("Decisão conflitante para orçamento já decidido.");
        }

        var updated = existing.Decide(approved);
        try
        {
            await dynamo.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = _table,
                Key = Key(workOrderId, version),
                ConditionExpression = "#status = :previous",
                UpdateExpression = "SET #status = :new",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "status" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":previous"] = new() { S = BudgetStatus.AwaitingApproval.ToString() },
                    [":new"] = new() { S = updated.Status.ToString() }
                }
            }, cancellationToken);
            return updated;
        }
        catch (ConditionalCheckFailedException)
        {
            var current = await GetAsync(workOrderId, version, cancellationToken);
            if (current?.Status == updated.Status) return current;
            throw new InvalidOperationException("Decisão conflitante após atualização concorrente.");
        }
    }

    private static Dictionary<string, AttributeValue> Key(Guid workOrderId, int version) => new()
    {
        ["pk"] = new() { S = $"OS#{workOrderId:N}" },
        ["sk"] = new() { S = $"BUDGET#{version:D6}" }
    };

    private static Dictionary<string, AttributeValue> InboxKey(Guid eventId) => new()
    {
        ["pk"] = new() { S = $"EVENT#{eventId:N}" },
        ["sk"] = new() { S = "RECEIVED" }
    };

    private static Dictionary<string, AttributeValue> ToItem(Budget budget)
    {
        var item = Key(budget.WorkOrderId, budget.Version);
        item["branchId"] = new AttributeValue { S = budget.BranchId ?? string.Empty };
        item["totalCents"] = new AttributeValue { N = budget.TotalCents.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        item["status"] = new AttributeValue { S = budget.Status.ToString() };
        item["lines"] = new AttributeValue { S = JsonSerializer.Serialize(budget.Lines) };
        item["createdAtUtc"] = new AttributeValue { S = budget.CreatedAtUtc.ToString("O") };
        return item;
    }

    private static Budget FromItem(Dictionary<string, AttributeValue> item)
    {
        var pk = item["pk"].S;
        var sk = item["sk"].S;
        return new Budget(
            Guid.ParseExact(pk[3..], "N"),
            int.Parse(sk[7..], System.Globalization.CultureInfo.InvariantCulture),
            string.IsNullOrEmpty(item["branchId"].S) ? null : item["branchId"].S,
            long.Parse(item["totalCents"].N, System.Globalization.CultureInfo.InvariantCulture),
            Enum.Parse<BudgetStatus>(item["status"].S),
            JsonSerializer.Deserialize<BudgetLine[]>(item["lines"].S) ?? [],
            DateTime.Parse(item["createdAtUtc"].S, null, System.Globalization.DateTimeStyles.RoundtripKind));
    }
}
