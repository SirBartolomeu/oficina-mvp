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
            BillingMetrics.RecordBudgetCreated();
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

    public async Task<Budget> CreateFromEventAsync(Guid eventId, Budget proposed, BudgetReadyEnvelope ready,
        CancellationToken cancellationToken)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("Identificador do evento obrigatório.");
        if (ready.EventId == Guid.Empty || ready.EventType != "BudgetReady" ||
            ready.CausationId != eventId || ready.WorkOrderId != proposed.WorkOrderId ||
            ready.BranchId != proposed.BranchId || ready.Payload.BudgetId != proposed.Id ||
            ready.Payload.BudgetVersion != proposed.Version ||
            ready.Payload.TotalCents != proposed.TotalCents || ready.Payload.Currency != "BRL")
            throw new ArgumentException("BudgetReady não corresponde ao orçamento criado.");
        var inboxItem = InboxKey(eventId);
        inboxItem["receivedAtUtc"] = new AttributeValue { S = DateTime.UtcNow.ToString("O") };
        var outboxItem = OutboxKey(ready.EventId);
        outboxItem["body"] = new AttributeValue { S = SqsTraceContext.Stamp(JsonSerializer.Serialize(ready,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))) };
        outboxItem["occurredAtUtc"] = new AttributeValue { S = ready.OccurredAtUtc.ToString("O") };
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
                    } },
                    new TransactWriteItem { Put = new Put
                    {
                        TableName = _table, Item = outboxItem,
                        ConditionExpression = "attribute_not_exists(pk)"
                    } }
                ]
            }, cancellationToken);
            BillingMetrics.RecordBudgetCreated();
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

    public async Task<IReadOnlyCollection<BillingOutboxMessage>> GetPendingOutboxAsync(
        CancellationToken cancellationToken)
    {
        var response = await dynamo.QueryAsync(new QueryRequest
        {
            TableName = _table,
            KeyConditionExpression = "pk = :pk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new() { S = "OUTBOX#PENDING" }
            },
            Limit = 10,
            ConsistentRead = true
        }, cancellationToken);
        return (response.Items ?? []).Select(item => new BillingOutboxMessage(
            Guid.ParseExact(item["sk"].S, "N"), item["body"].S)).ToArray();
    }

    public Task MarkOutboxPublishedAsync(Guid eventId, CancellationToken cancellationToken) =>
        dynamo.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = _table,
            Key = OutboxKey(eventId)
        }, cancellationToken);

    public async Task<Guid?> TryClaimOutboxAsync(Guid eventId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var claimId = Guid.NewGuid();
        try
        {
            await dynamo.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = _table,
                Key = OutboxKey(eventId),
                ConditionExpression = "attribute_exists(pk) AND (attribute_not_exists(leaseUntil) OR leaseUntil < :now)",
                UpdateExpression = "SET leaseOwner = :owner, leaseUntil = :until",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":now"] = new() { N = now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    [":until"] = new() { N = now.AddMinutes(2).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    [":owner"] = new() { S = claimId.ToString("N") }
                }
            }, cancellationToken);
            return claimId;
        }
        catch (ConditionalCheckFailedException)
        {
            return null;
        }
    }

    public Task MarkClaimedOutboxPublishedAsync(Guid eventId, Guid claimId,
        CancellationToken cancellationToken) =>
        dynamo.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = _table,
            Key = OutboxKey(eventId),
            ConditionExpression = "leaseOwner = :owner",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":owner"] = new() { S = claimId.ToString("N") }
            }
        }, cancellationToken);

    public Task ReleaseOutboxClaimAsync(Guid eventId, Guid claimId,
        CancellationToken cancellationToken) =>
        dynamo.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = _table,
            Key = OutboxKey(eventId),
            ConditionExpression = "leaseOwner = :owner",
            UpdateExpression = "REMOVE leaseOwner, leaseUntil",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":owner"] = new() { S = claimId.ToString("N") }
            }
        }, cancellationToken);

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

    public async Task<bool> RecordPaymentRequestAsync(Guid eventId, PaymentRequest payment,
        CancellationToken cancellationToken)
    {
        var paymentKey = PaymentKey(payment.Id);
        paymentKey["workOrderId"] = new AttributeValue { S = payment.WorkOrderId.ToString("D") };
        paymentKey["budgetId"] = new AttributeValue { S = payment.BudgetId.ToString("D") };
        paymentKey["budgetVersion"] = new AttributeValue { N = payment.BudgetVersion.ToString() };
        paymentKey["branchId"] = new AttributeValue { S = payment.BranchId ?? string.Empty };
        paymentKey["totalCents"] = new AttributeValue { N = payment.TotalCents.ToString() };
        paymentKey["status"] = new AttributeValue { S = "Requested" };
        paymentKey["requestedAtUtc"] = new AttributeValue { S = payment.RequestedAtUtc.ToString("O") };
        var pending = PendingPaymentKey(payment.Id);
        pending["requestedAtUtc"] = new AttributeValue { S = payment.RequestedAtUtc.ToString("O") };
        var inbox = InboxKey(eventId);
        inbox["receivedAtUtc"] = new AttributeValue { S = DateTime.UtcNow.ToString("O") };
        try
        {
            await dynamo.TransactWriteItemsAsync(new TransactWriteItemsRequest
            {
                TransactItems =
                [
                    new TransactWriteItem { Put = new Put
                    {
                        TableName = _table, Item = paymentKey,
                        ConditionExpression = "attribute_not_exists(pk)"
                    } },
                    new TransactWriteItem { Put = new Put
                    {
                        TableName = _table, Item = inbox,
                        ConditionExpression = "attribute_not_exists(pk)"
                    } },
                    new TransactWriteItem { Put = new Put
                    {
                        TableName = _table, Item = pending,
                        ConditionExpression = "attribute_not_exists(pk)"
                    } }
                ]
            }, cancellationToken);
            return true;
        }
        catch (TransactionCanceledException)
        {
            var received = await dynamo.GetItemAsync(new GetItemRequest
            {
                TableName = _table, Key = InboxKey(eventId), ConsistentRead = true
            }, cancellationToken);
            if (received.Item is not { Count: > 0 })
                throw new InvalidOperationException("Tentativa de pagamento já usada por outro evento.");
            var existing = await GetPaymentRequestAsync(payment.Id, cancellationToken);
            if (existing != payment)
                throw new InvalidOperationException("Evento de pagamento repetido com conteúdo diferente.");
            return false;
        }
    }

    public async Task<PaymentRequest?> GetPaymentRequestAsync(Guid paymentAttemptId,
        CancellationToken cancellationToken)
    {
        var response = await dynamo.GetItemAsync(new GetItemRequest
        {
            TableName = _table, Key = PaymentKey(paymentAttemptId), ConsistentRead = true
        }, cancellationToken);
        if (response.Item is not { Count: > 0 }) return null;
        var item = response.Item;
        return new PaymentRequest(paymentAttemptId, Guid.Parse(item["workOrderId"].S),
            Guid.Parse(item["budgetId"].S), int.Parse(item["budgetVersion"].N),
            string.IsNullOrEmpty(item["branchId"].S) ? null : item["branchId"].S,
            long.Parse(item["totalCents"].N), DateTime.Parse(item["requestedAtUtc"].S,
                null, System.Globalization.DateTimeStyles.RoundtripKind));
    }

    public async Task<IReadOnlyCollection<Guid>> GetPendingPaymentsAsync(CancellationToken cancellationToken)
    {
        var response = await dynamo.QueryAsync(new QueryRequest
        {
            TableName = _table,
            KeyConditionExpression = "pk = :pk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new() { S = "PAYMENT#PENDING" }
            },
            Limit = 10,
            ConsistentRead = true
        }, cancellationToken);
        return (response.Items ?? []).Select(item => Guid.ParseExact(item["sk"].S, "N")).ToArray();
    }

    public async Task<PaymentAttempt?> GetPaymentAttemptAsync(Guid paymentAttemptId,
        CancellationToken cancellationToken)
    {
        var response = await dynamo.GetItemAsync(new GetItemRequest
        {
            TableName = _table, Key = PaymentKey(paymentAttemptId), ConsistentRead = true
        }, cancellationToken);
        if (response.Item is not { Count: > 0 }) return null;
        var item = response.Item;
        return new PaymentAttempt(paymentAttemptId, item["status"].S,
            item.TryGetValue("providerOrderId", out var id) ? id.S : null,
            item.TryGetValue("qrData", out var qr) ? qr.S : null)
        {
            CheckoutUrl = item.TryGetValue("checkoutUrl", out var checkout) ? checkout.S : null,
            TotalCents = long.Parse(item["totalCents"].N, System.Globalization.CultureInfo.InvariantCulture),
            RefundRequestId = item.TryGetValue("refundRequestId", out var refundId)
                ? Guid.Parse(refundId.S) : null,
            RefundReason = item.TryGetValue("refundReason", out var refundReason)
                ? refundReason.S : null,
            RefundSubmittedAtUtc = item.TryGetValue("refundSubmittedAtUtc", out var submitted)
                ? DateTime.Parse(submitted.S, null,
                    System.Globalization.DateTimeStyles.RoundtripKind) : null
        };
    }

    public async Task<bool> BeginRefundSubmissionAsync(Guid paymentAttemptId,
        Guid refundRequestId, CancellationToken cancellationToken)
    {
        try
        {
            await dynamo.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = _table, Key = PaymentKey(paymentAttemptId),
                ConditionExpression = "#status = :requested AND refundRequestId = :refundId",
                UpdateExpression = "SET #status = :submitting, refundSubmittedAtUtc = :at",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "status" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":requested"] = new() { S = "RefundRequested" },
                    [":submitting"] = new() { S = "RefundSubmitting" },
                    [":refundId"] = new() { S = refundRequestId.ToString("D") },
                    [":at"] = new() { S = DateTime.UtcNow.ToString("O") }
                }
            }, cancellationToken);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            var current = await GetPaymentAttemptAsync(paymentAttemptId, cancellationToken);
            if (current?.Status == "RefundSubmitting" &&
                current.RefundRequestId == refundRequestId) return false;
            throw new InvalidOperationException("Conflito ao iniciar estorno.");
        }
    }

    public async Task<bool> ConfirmRefundAsync(PaymentRequest payment,
        Guid refundRequestId, string providerOrderId, CancellationToken cancellationToken)
    {
        var eventId = Guid.NewGuid();
        var occurredAt = DateTime.UtcNow;
        var confirmed = new RefundConfirmedEnvelope(eventId, "RefundConfirmed", 1,
            payment.WorkOrderId, refundRequestId, payment.WorkOrderId, payment.BranchId,
            occurredAt, new RefundConfirmedPayload(payment.Id, refundRequestId,
                providerOrderId, payment.TotalCents, "BRL"));
        var outbox = OutboxKey(eventId);
        outbox["body"] = new AttributeValue { S = SqsTraceContext.Stamp(JsonSerializer.Serialize(confirmed,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))) };
        outbox["occurredAtUtc"] = new AttributeValue { S = occurredAt.ToString("O") };
        try
        {
            await dynamo.TransactWriteItemsAsync(new TransactWriteItemsRequest
            {
                TransactItems =
                [
                    new TransactWriteItem { Update = new Update
                    {
                        TableName = _table, Key = PaymentKey(payment.Id),
                        ConditionExpression = "#status = :submitting AND refundRequestId = :refundId AND providerOrderId = :orderId",
                        UpdateExpression = "SET #status = :refunded, refundedAtUtc = :at",
                        ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "status" },
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":submitting"] = new() { S = "RefundSubmitting" },
                            [":refunded"] = new() { S = "Refunded" },
                            [":refundId"] = new() { S = refundRequestId.ToString("D") },
                            [":orderId"] = new() { S = providerOrderId },
                            [":at"] = new() { S = occurredAt.ToString("O") }
                        }
                    } },
                    new TransactWriteItem { Delete = new Delete
                    {
                        TableName = _table, Key = PendingRefundKey(payment.Id),
                        ConditionExpression = "attribute_exists(pk)"
                    } },
                    new TransactWriteItem { Put = new Put
                    {
                        TableName = _table, Item = outbox,
                        ConditionExpression = "attribute_not_exists(pk)"
                    } }
                ]
            }, cancellationToken);
            return true;
        }
        catch (TransactionCanceledException)
        {
            var current = await GetPaymentAttemptAsync(payment.Id, cancellationToken);
            if (current?.Status == "Refunded" && current.RefundRequestId == refundRequestId &&
                current.ProviderOrderId == providerOrderId) return false;
            throw new InvalidOperationException("Conflito ao confirmar estorno.");
        }
    }

    public async Task<bool> RecordRefundRequestAsync(Guid refundRequestId,
        Guid paymentAttemptId, string reason, CancellationToken cancellationToken)
    {
        var inbox = InboxKey(refundRequestId);
        inbox["receivedAtUtc"] = new AttributeValue { S = DateTime.UtcNow.ToString("O") };
        var pending = PendingRefundKey(paymentAttemptId);
        pending["refundRequestId"] = new AttributeValue { S = refundRequestId.ToString("D") };
        pending["requestedAtUtc"] = new AttributeValue { S = DateTime.UtcNow.ToString("O") };
        try
        {
            await dynamo.TransactWriteItemsAsync(new TransactWriteItemsRequest
            {
                TransactItems =
                [
                    new TransactWriteItem { Update = new Update
                    {
                        TableName = _table, Key = PaymentKey(paymentAttemptId),
                        ConditionExpression = "#status = :confirmed AND attribute_exists(providerOrderId) AND attribute_not_exists(refundRequestId)",
                        UpdateExpression = "SET #status = :requested, refundRequestId = :refundId, refundReason = :reason",
                        ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "status" },
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":confirmed"] = new() { S = "Confirmed" },
                            [":requested"] = new() { S = "RefundRequested" },
                            [":refundId"] = new() { S = refundRequestId.ToString("D") },
                            [":reason"] = new() { S = reason }
                        }
                    } },
                    new TransactWriteItem { Put = new Put
                    {
                        TableName = _table, Item = inbox,
                        ConditionExpression = "attribute_not_exists(pk)"
                    } },
                    new TransactWriteItem { Put = new Put
                    {
                        TableName = _table, Item = pending,
                        ConditionExpression = "attribute_not_exists(pk)"
                    } }
                ]
            }, cancellationToken);
            return true;
        }
        catch (TransactionCanceledException)
        {
            var current = await GetPaymentAttemptAsync(paymentAttemptId, cancellationToken);
            if (current?.Status == "RefundRequested" &&
                current.RefundRequestId == refundRequestId &&
                current.RefundReason == reason) return false;
            throw new InvalidOperationException("Conflito ao solicitar estorno.");
        }
    }

    public async Task<IReadOnlyCollection<Guid>> GetPendingRefundsAsync(
        CancellationToken cancellationToken)
    {
        var response = await dynamo.QueryAsync(new QueryRequest
        {
            TableName = _table,
            KeyConditionExpression = "pk = :pk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new() { S = "REFUND#PENDING" }
            },
            Limit = 10,
            ConsistentRead = true
        }, cancellationToken);
        return (response.Items ?? []).Select(item => Guid.ParseExact(item["sk"].S,
            "N")).ToArray();
    }

    public async Task<bool> CompletePaymentCreationAsync(Guid paymentAttemptId,
        MercadoPagoQrOrder order, CancellationToken cancellationToken)
    {
        try
        {
            await dynamo.TransactWriteItemsAsync(new TransactWriteItemsRequest
            {
                TransactItems =
                [
                    new TransactWriteItem { Update = new Update
                    {
                        TableName = _table,
                        Key = PaymentKey(paymentAttemptId),
                        ConditionExpression = "#status = :requested",
                        UpdateExpression = "SET #status = :created, providerOrderId = :orderId, qrData = :qrData, checkoutUrl = :checkoutUrl",
                        ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "status" },
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":requested"] = new() { S = "Requested" },
                            [":created"] = new() { S = "Created" },
                            [":orderId"] = new() { S = order.Id },
                            [":qrData"] = order.QrData is null ? new() { NULL = true } : new() { S = order.QrData },
                            [":checkoutUrl"] = order.CheckoutUrl is null ? new() { NULL = true } : new() { S = order.CheckoutUrl }
                        }
                    } },
                    new TransactWriteItem { Delete = new Delete
                    {
                        TableName = _table, Key = PendingPaymentKey(paymentAttemptId),
                        ConditionExpression = "attribute_exists(pk)"
                    } }
                ]
            }, cancellationToken);
            return true;
        }
        catch (TransactionCanceledException)
        {
            var current = await GetPaymentAttemptAsync(paymentAttemptId, cancellationToken);
            if (current?.Status == "Created" && current.ProviderOrderId == order.Id)
                return false;
            throw new InvalidOperationException("Conflito ao concluir a criação da order.");
        }
    }

    public async Task MarkPaymentManualReviewAsync(Guid paymentAttemptId, CancellationToken cancellationToken)
    {
        await dynamo.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem { Update = new Update
                {
                    TableName = _table, Key = PaymentKey(paymentAttemptId),
                    ConditionExpression = "#status = :requested",
                    UpdateExpression = "SET #status = :review",
                    ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "status" },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":requested"] = new() { S = "Requested" },
                        [":review"] = new() { S = "ManualReview" }
                    }
                } },
                new TransactWriteItem { Delete = new Delete
                {
                    TableName = _table, Key = PendingPaymentKey(paymentAttemptId),
                    ConditionExpression = "attribute_exists(pk)"
                } }
            ]
        }, cancellationToken);
    }

    public async Task<bool> ConfirmPaymentAsync(PaymentRequest payment, string providerOrderId,
        CancellationToken cancellationToken)
    {
        var eventId = Guid.NewGuid();
        var occurredAt = DateTime.UtcNow;
        var confirmation = new PaymentConfirmedEnvelope(eventId, "PaymentConfirmed", 1,
            payment.WorkOrderId, Guid.NewGuid(), payment.WorkOrderId, payment.BranchId,
            occurredAt, new PaymentConfirmedPayload(payment.Id, providerOrderId,
                payment.TotalCents, "BRL"));
        var outbox = OutboxKey(eventId);
        outbox["body"] = new AttributeValue { S = SqsTraceContext.Stamp(JsonSerializer.Serialize(confirmation,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))) };
        outbox["occurredAtUtc"] = new AttributeValue { S = occurredAt.ToString("O") };
        try
        {
            await dynamo.TransactWriteItemsAsync(new TransactWriteItemsRequest
            {
                TransactItems =
                [
                    new TransactWriteItem { Update = new Update
                    {
                        TableName = _table, Key = PaymentKey(payment.Id),
                        ConditionExpression = "#status = :created AND providerOrderId = :providerOrderId",
                        UpdateExpression = "SET #status = :confirmed",
                        ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "status" },
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":created"] = new() { S = "Created" },
                            [":confirmed"] = new() { S = "Confirmed" },
                            [":providerOrderId"] = new() { S = providerOrderId }
                        }
                    } },
                    new TransactWriteItem { Put = new Put
                    {
                        TableName = _table, Item = outbox,
                        ConditionExpression = "attribute_not_exists(pk)"
                    } }
                ]
            }, cancellationToken);
            return true;
        }
        catch (TransactionCanceledException)
        {
            var current = await GetPaymentAttemptAsync(payment.Id, cancellationToken);
            if (current?.Status == "Confirmed" && current.ProviderOrderId == providerOrderId)
                return false;
            throw new InvalidOperationException("Conflito ao confirmar pagamento.");
        }
    }

    public async Task<bool> RejectPaymentAsync(PaymentRequest payment, string providerOrderId,
        string reason, CancellationToken cancellationToken)
    {
        if (reason is not "expired" and not "canceled" and not "failed")
            throw new ArgumentException("Motivo de recusa inválido.");
        var eventId = Guid.NewGuid();
        var occurredAt = DateTime.UtcNow;
        var rejection = new PaymentRejectedEnvelope(eventId, "PaymentRejected", 1,
            payment.WorkOrderId, Guid.NewGuid(), payment.WorkOrderId, payment.BranchId,
            occurredAt, new PaymentRejectedPayload(payment.Id, providerOrderId,
                payment.TotalCents, "BRL", reason));
        var outbox = OutboxKey(eventId);
        outbox["body"] = new AttributeValue { S = SqsTraceContext.Stamp(JsonSerializer.Serialize(rejection,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))) };
        outbox["occurredAtUtc"] = new AttributeValue { S = occurredAt.ToString("O") };
        try
        {
            await dynamo.TransactWriteItemsAsync(new TransactWriteItemsRequest
            {
                TransactItems =
                [
                    new TransactWriteItem { Update = new Update
                    {
                        TableName = _table, Key = PaymentKey(payment.Id),
                        ConditionExpression = "#status = :created AND providerOrderId = :providerOrderId",
                        UpdateExpression = "SET #status = :rejected, rejectionReason = :reason",
                        ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "status" },
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":created"] = new() { S = "Created" },
                            [":rejected"] = new() { S = "Rejected" },
                            [":providerOrderId"] = new() { S = providerOrderId },
                            [":reason"] = new() { S = reason }
                        }
                    } },
                    new TransactWriteItem { Put = new Put
                    {
                        TableName = _table, Item = outbox,
                        ConditionExpression = "attribute_not_exists(pk)"
                    } }
                ]
            }, cancellationToken);
            return true;
        }
        catch (TransactionCanceledException)
        {
            var current = await GetPaymentAttemptAsync(payment.Id, cancellationToken);
            if (current?.Status == "Rejected" && current.ProviderOrderId == providerOrderId)
                return false;
            throw new InvalidOperationException("Conflito ao recusar pagamento.");
        }
    }

    public async Task MarkCreatedPaymentManualReviewAsync(Guid paymentAttemptId,
        CancellationToken cancellationToken)
    {
        try
        {
            await dynamo.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = _table, Key = PaymentKey(paymentAttemptId),
                ConditionExpression = "#status = :created",
                UpdateExpression = "SET #status = :review",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "status" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":created"] = new() { S = "Created" },
                    [":review"] = new() { S = "ManualReview" }
                }
            }, cancellationToken);
        }
        catch (ConditionalCheckFailedException)
        {
            var current = await GetPaymentAttemptAsync(paymentAttemptId, cancellationToken);
            if (current?.Status != "ManualReview") throw;
        }
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
        var decisionId = Guid.NewGuid();
        var occurredAt = DateTime.UtcNow;
        var decision = new BudgetDecisionEnvelope(decisionId,
            approved ? "BudgetApproved" : "BudgetRefused", 1, workOrderId, Guid.NewGuid(),
            workOrderId, existing.BranchId, occurredAt,
            new BudgetDecisionPayload(existing.Id, version, decisionId, null));
        var outboxItem = OutboxKey(decisionId);
        outboxItem["body"] = new AttributeValue { S = SqsTraceContext.Stamp(JsonSerializer.Serialize(decision,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))) };
        outboxItem["occurredAtUtc"] = new AttributeValue { S = occurredAt.ToString("O") };
        try
        {
            await dynamo.TransactWriteItemsAsync(new TransactWriteItemsRequest
            {
                TransactItems =
                [
                    new TransactWriteItem { Update = new Update
                    {
                        TableName = _table,
                        Key = Key(workOrderId, version),
                        ConditionExpression = "#status = :previous",
                        UpdateExpression = "SET #status = :new, decisionId = :decisionId",
                        ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "status" },
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":previous"] = new() { S = BudgetStatus.AwaitingApproval.ToString() },
                            [":new"] = new() { S = updated.Status.ToString() },
                            [":decisionId"] = new() { S = decisionId.ToString("D") }
                        }
                    } },
                    new TransactWriteItem { Put = new Put
                    {
                        TableName = _table, Item = outboxItem,
                        ConditionExpression = "attribute_not_exists(pk)"
                    } }
                ]
            }, cancellationToken);
            BillingMetrics.RecordBudgetDecided(approved);
            return updated;
        }
        catch (TransactionCanceledException)
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

    private static Dictionary<string, AttributeValue> PaymentKey(Guid paymentAttemptId) => new()
    {
        ["pk"] = new() { S = $"PAYMENT#{paymentAttemptId:N}" },
        ["sk"] = new() { S = "REQUEST" }
    };

    private static Dictionary<string, AttributeValue> PendingPaymentKey(Guid paymentAttemptId) => new()
    {
        ["pk"] = new() { S = "PAYMENT#PENDING" },
        ["sk"] = new() { S = paymentAttemptId.ToString("N") }
    };

    private static Dictionary<string, AttributeValue> PendingRefundKey(Guid paymentAttemptId) => new()
    {
        ["pk"] = new() { S = "REFUND#PENDING" },
        ["sk"] = new() { S = paymentAttemptId.ToString("N") }
    };

    private static Dictionary<string, AttributeValue> OutboxKey(Guid eventId) => new()
    {
        ["pk"] = new() { S = "OUTBOX#PENDING" },
        ["sk"] = new() { S = eventId.ToString("N") }
    };

    private static Dictionary<string, AttributeValue> ToItem(Budget budget)
    {
        var item = Key(budget.WorkOrderId, budget.Version);
        item["budgetId"] = new AttributeValue { S = budget.Id.ToString("D") };
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
            DateTime.Parse(item["createdAtUtc"].S, null, System.Globalization.DateTimeStyles.RoundtripKind))
        {
            Id = item.TryGetValue("budgetId", out var budgetId) ? Guid.Parse(budgetId.S) : Guid.Empty
        };
    }
}

public sealed record BillingOutboxMessage(Guid EventId, string Body);
