using System.Text.Json;

namespace Oficina.Billing;

public sealed record RefundPaymentRequestedPayload(Guid PaymentAttemptId,
    Guid RefundRequestId, string Reason);
public sealed record RefundPaymentRequestedEnvelope(Guid EventId, string EventType,
    int SchemaVersion, Guid CorrelationId, Guid CausationId, Guid WorkOrderId,
    string? BranchId, DateTime OccurredAtUtc, RefundPaymentRequestedPayload Payload);

public sealed class RefundRequestHandler(BillingRepository repository)
{
    public async Task<bool> HandleAsync(string body, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<RefundPaymentRequestedEnvelope>(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new ArgumentException("Pedido de estorno vazio.");
        if (envelope.EventType != "RefundPaymentRequested" || envelope.SchemaVersion != 1 ||
            envelope.EventId == Guid.Empty || envelope.Payload is null ||
            envelope.EventId != envelope.Payload.RefundRequestId ||
            envelope.CorrelationId == Guid.Empty || envelope.CausationId == Guid.Empty ||
            envelope.WorkOrderId == Guid.Empty || envelope.Payload.PaymentAttemptId == Guid.Empty ||
            string.IsNullOrWhiteSpace(envelope.Payload.Reason) ||
            envelope.Payload.Reason.Length > 180)
            throw new ArgumentException("Contrato de estorno inválido.");
        var payment = await repository.GetPaymentRequestAsync(
            envelope.Payload.PaymentAttemptId, cancellationToken)
            ?? throw new InvalidOperationException("Pagamento para estorno não encontrado.");
        if (payment.WorkOrderId != envelope.WorkOrderId ||
            payment.BranchId != envelope.BranchId)
            throw new InvalidOperationException("Estorno não corresponde ao pagamento da OS.");
        return await repository.RecordRefundRequestAsync(envelope.EventId,
            envelope.Payload.PaymentAttemptId, envelope.Payload.Reason,
            cancellationToken);
    }
}
