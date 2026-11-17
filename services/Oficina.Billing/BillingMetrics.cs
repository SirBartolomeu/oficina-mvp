using System.Diagnostics.Metrics;

namespace Oficina.Billing;

internal static class BillingMetrics
{
    public const string MeterName = "Oficina.Billing.Business";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> BudgetsCreated = Meter.CreateCounter<long>("billing_budgets_created", "{budget}");
    private static readonly Counter<long> BudgetsDecided = Meter.CreateCounter<long>("billing_budgets_decided", "{budget}");
    private static readonly Counter<long> AcceptedCommands = Meter.CreateCounter<long>("billing_commands_accepted", "{command}");
    private static readonly Histogram<double> MessageDuration = Meter.CreateHistogram<double>(
        "sqs_message_processing_duration", "s");
    private static readonly Counter<long> MessageFailures = Meter.CreateCounter<long>("sqs_message_failures");
    private static readonly Counter<long> ReceiveFailures = Meter.CreateCounter<long>("sqs_receive_failures");

    public static void RecordBudgetCreated() => BudgetsCreated.Add(1);
    public static void RecordBudgetDecided(bool approved) =>
        BudgetsDecided.Add(1, new KeyValuePair<string, object?>("decision", approved ? "approved" : "refused"));
    public static void RecordAcceptedCommand(string eventType) =>
        AcceptedCommands.Add(1, new KeyValuePair<string, object?>("event.type", eventType));

    public static void RecordSqsProcessed(string eventType, double seconds) =>
        MessageDuration.Record(seconds, new KeyValuePair<string, object?>("event.type", eventType));

    public static void RecordSqsMessageFailure() => MessageFailures.Add(1);
    public static void RecordSqsReceiveFailure() => ReceiveFailures.Add(1);
}
