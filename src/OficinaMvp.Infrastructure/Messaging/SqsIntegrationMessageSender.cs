using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace OficinaMvp.Infrastructure.Messaging;

public sealed class SqsIntegrationMessageSender(IAmazonSQS sqs, string queueUrl) : IIntegrationMessageSender
{
    public async Task SendAsync(Guid eventId, string eventType, string payloadJson, CancellationToken cancellationToken)
    {
        var request = new SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = payloadJson,
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["eventType"] = new() { DataType = "String", StringValue = eventType }
            }
        };
        if (queueUrl.EndsWith(".fifo", StringComparison.OrdinalIgnoreCase))
        {
            using var document = JsonDocument.Parse(payloadJson);
            request.MessageGroupId = document.RootElement.GetProperty("workOrderId").GetString();
            request.MessageDeduplicationId = eventId.ToString("N");
        }
        await sqs.SendMessageAsync(request, cancellationToken);
    }
}
