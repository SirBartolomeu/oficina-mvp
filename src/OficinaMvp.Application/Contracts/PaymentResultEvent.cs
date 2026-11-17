namespace OficinaMvp.Application.Contracts;

public sealed record PaymentConfirmedPayload(Guid PaymentAttemptId, string ProviderOrderId,
    long TotalCents, string Currency);
public sealed record PaymentRejectedPayload(Guid PaymentAttemptId, string ProviderOrderId,
    long TotalCents, string Currency, string Reason);
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
public sealed record RefundPaymentRequestedPayload(Guid PaymentAttemptId,
    Guid RefundRequestId, string Reason);
public sealed record RefundConfirmedPayload(Guid PaymentAttemptId, Guid RefundRequestId,
    string ProviderOrderId, long TotalCents, string Currency);
