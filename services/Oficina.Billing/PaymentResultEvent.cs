namespace Oficina.Billing;

public sealed record PaymentConfirmedPayload(Guid PaymentAttemptId, string ProviderOrderId,
    long TotalCents, string Currency);
public sealed record PaymentConfirmedEnvelope(Guid EventId, string EventType, int SchemaVersion,
    Guid CorrelationId, Guid CausationId, Guid WorkOrderId, string? BranchId,
    DateTime OccurredAtUtc, PaymentConfirmedPayload Payload);
public sealed record PaymentRejectedPayload(Guid PaymentAttemptId, string ProviderOrderId,
    long TotalCents, string Currency, string Reason);
public sealed record PaymentRejectedEnvelope(Guid EventId, string EventType, int SchemaVersion,
    Guid CorrelationId, Guid CausationId, Guid WorkOrderId, string? BranchId,
    DateTime OccurredAtUtc, PaymentRejectedPayload Payload);
public sealed record RefundConfirmedPayload(Guid PaymentAttemptId, Guid RefundRequestId,
    string ProviderOrderId, long TotalCents, string Currency);
public sealed record RefundConfirmedEnvelope(Guid EventId, string EventType, int SchemaVersion,
    Guid CorrelationId, Guid CausationId, Guid WorkOrderId, string? BranchId,
    DateTime OccurredAtUtc, RefundConfirmedPayload Payload);
