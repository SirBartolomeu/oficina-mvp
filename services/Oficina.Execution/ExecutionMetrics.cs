using System.Diagnostics.Metrics;

namespace Oficina.Execution;

internal static class ExecutionMetrics
{
    public const string MeterName = "Oficina.Execution.Business";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> AppliedCommands = Meter.CreateCounter<long>(
        "execution_commands_applied", "{command}");
    private static readonly Histogram<double> MessageDuration = Meter.CreateHistogram<double>(
        "sqs_message_processing_duration", "s");
    private static readonly Counter<long> MessageFailures = Meter.CreateCounter<long>("sqs_message_failures");
    private static readonly Counter<long> ReceiveFailures = Meter.CreateCounter<long>("sqs_receive_failures");

    public static void RecordAppliedCommand(string eventType) =>
        AppliedCommands.Add(1, new KeyValuePair<string, object?>("event.type", eventType));

    public static void RecordSqsProcessed(string eventType, double seconds) =>
        MessageDuration.Record(seconds, new KeyValuePair<string, object?>("event.type", eventType));

    public static void RecordSqsMessageFailure() => MessageFailures.Add(1);
    public static void RecordSqsReceiveFailure() => ReceiveFailures.Add(1);
}
