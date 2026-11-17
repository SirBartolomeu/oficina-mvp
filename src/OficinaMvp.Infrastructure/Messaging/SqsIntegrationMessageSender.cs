using System.Diagnostics;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace OficinaMvp.Infrastructure.Messaging;

public sealed class SqsIntegrationMessageSender : IIntegrationMessageSender
{
    private readonly IAmazonSQS _sqs;
    private readonly IReadOnlyDictionary<string, string> _queueUrls;

    public SqsIntegrationMessageSender(IAmazonSQS sqs, string billingQueueUrl)
        : this(sqs, new Dictionary<string, string> { ["WorkOrderOpened"] = billingQueueUrl }) { }

    public SqsIntegrationMessageSender(IAmazonSQS sqs, IReadOnlyDictionary<string, string> queueUrls)
    {
        _sqs = sqs;
        _queueUrls = queueUrls;
    }

    public async Task SendAsync(Guid eventId, string eventType, string payloadJson, CancellationToken cancellationToken)
    {
        if (!_queueUrls.TryGetValue(eventType, out var queueUrl) || string.IsNullOrWhiteSpace(queueUrl))
            throw new InvalidOperationException($"Fila SQS não configurada para {eventType}.");
        var storedParent = SqsTraceContext.Read(payloadJson);
        using var activity = SqsTraceContext.Start("SQS publish", ActivityKind.Producer, storedParent);
        activity.SetTag("messaging.system", "aws_sqs");
        activity.SetTag("messaging.operation.name", "publish");
        activity.SetTag("messaging.message.type", eventType);
        var request = new SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = payloadJson,
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["eventType"] = new() { DataType = "String", StringValue = eventType },
                ["traceparent"] = new() { DataType = "String", StringValue = activity.Id }
            }
        };
        if (queueUrl.EndsWith(".fifo", StringComparison.OrdinalIgnoreCase))
        {
            using var document = JsonDocument.Parse(payloadJson);
            request.MessageGroupId = document.RootElement.GetProperty("workOrderId").GetString();
            request.MessageDeduplicationId = eventId.ToString("N");
        }
        await _sqs.SendMessageAsync(request, cancellationToken);
    }
}
