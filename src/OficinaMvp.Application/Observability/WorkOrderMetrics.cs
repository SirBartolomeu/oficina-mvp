using System.Diagnostics.Metrics;

namespace OficinaMvp.Application.Observability;

public static class WorkOrderMetrics
{
    public const string MeterName = "Oficina.WorkOrders";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Opened = Meter.CreateCounter<long>("work_orders_opened", "{order}");
    private static readonly Counter<long> SagaTransitions = Meter.CreateCounter<long>("work_order_saga_transitions", "{transition}");
    private static readonly Histogram<double> MessageDuration = Meter.CreateHistogram<double>(
        "sqs_message_processing_duration", "s");
    private static readonly Counter<long> MessageFailures = Meter.CreateCounter<long>("sqs_message_failures");
    private static readonly Counter<long> ReceiveFailures = Meter.CreateCounter<long>("sqs_receive_failures");
    private static readonly Histogram<double> ProcessStateDuration = Meter.CreateHistogram<double>(
        "work_order_process_state_duration", "s");

    public static void RecordOpened() => Opened.Add(1);

    public static void RecordSagaTransition(string eventType) =>
        SagaTransitions.Add(1, new KeyValuePair<string, object?>("event.type", eventType));

    public static void RecordSqsProcessed(string eventType, double seconds) =>
        MessageDuration.Record(seconds, new KeyValuePair<string, object?>("event.type", eventType));

    public static void RecordSqsMessageFailure() => MessageFailures.Add(1);
    public static void RecordSqsReceiveFailure() => ReceiveFailures.Add(1);

    public static void RecordProcessStateDuration(string state, double seconds) =>
        ProcessStateDuration.Record(seconds, new KeyValuePair<string, object?>("state", state));
}
