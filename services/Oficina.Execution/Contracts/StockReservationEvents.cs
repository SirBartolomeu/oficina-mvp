namespace Oficina.Execution.Contracts;

public sealed record IntegrationEnvelope<T>(Guid EventId, string EventType, int SchemaVersion,
    Guid CorrelationId, Guid CausationId, Guid WorkOrderId, string? BranchId,
    DateTime OccurredAtUtc, T Payload);
public sealed record ReserveStockRequestedPayload(Guid ReservationId, int OrderVersion,
    IReadOnlyCollection<CatalogItemRequest> Parts, DateTime ExpiresAtUtc);
public sealed record StockReservationResultPayload(Guid ReservationId, int OrderVersion, string? Reason);
public sealed record ReleaseStockRequestedPayload(Guid ReservationId, string Reason);
public sealed record StockReleasedPayload(Guid ReservationId);
public sealed record ConsumeStockRequestedPayload(Guid ReservationId);
public sealed record StockConsumedPayload(Guid ReservationId);
public sealed record ExecutionRequestedPayload(Guid ExecutionId, Guid ReservationId,
    Guid PaymentAttemptId);
public sealed record ExecutionStartedPayload(Guid ExecutionId, Guid ReservationId,
    Guid PaymentAttemptId);
public sealed record ExecutionFinishedPayload(Guid ExecutionId, Guid ReservationId,
    Guid PaymentAttemptId);
public sealed record ExecutionRejectedPayload(Guid ExecutionId, Guid ReservationId,
    Guid PaymentAttemptId, string Reason);
