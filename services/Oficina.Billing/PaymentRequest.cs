using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Oficina.Billing;

public sealed record CreatePaymentRequestedPayload(
    Guid PaymentAttemptId, Guid BudgetId, int BudgetVersion, long TotalCents, string Currency);
public sealed record CreatePaymentRequestedEnvelope(Guid EventId, string EventType, int SchemaVersion,
    Guid CorrelationId, Guid CausationId, Guid WorkOrderId, string? BranchId,
    DateTime OccurredAtUtc, CreatePaymentRequestedPayload Payload);
public sealed record PaymentRequest(Guid Id, Guid WorkOrderId, Guid BudgetId, int BudgetVersion,
    string? BranchId, long TotalCents, DateTime RequestedAtUtc);
public sealed record PaymentAttempt(Guid Id, string Status, string? ProviderOrderId, string? QrData)
{
    public string? CheckoutUrl { get; init; }
    public long TotalCents { get; init; }
    public Guid? RefundRequestId { get; init; }
    public string? RefundReason { get; init; }
    public DateTime? RefundSubmittedAtUtc { get; init; }
}

public sealed class PaymentRequestHandler(BillingRepository repository)
{
    public async Task<bool> HandleAsync(string body, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<CreatePaymentRequestedEnvelope>(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new ArgumentException("Comando de pagamento vazio.");
        if (envelope.EventType != "CreatePaymentRequested" || envelope.SchemaVersion != 1 ||
            envelope.EventId == Guid.Empty || envelope.CorrelationId == Guid.Empty ||
            envelope.CausationId == Guid.Empty || envelope.WorkOrderId == Guid.Empty ||
            envelope.Payload is null || envelope.Payload.PaymentAttemptId == Guid.Empty ||
            envelope.Payload.BudgetId == Guid.Empty || envelope.Payload.BudgetVersion <= 0 ||
            envelope.Payload.TotalCents <= 0 || envelope.Payload.Currency != "BRL")
            throw new ArgumentException("Contrato de pagamento inválido.");

        var budget = await repository.GetAsync(envelope.WorkOrderId,
            envelope.Payload.BudgetVersion, cancellationToken)
            ?? throw new InvalidOperationException("Orçamento do pagamento não encontrado.");
        if (budget.Id != envelope.Payload.BudgetId || budget.BranchId != envelope.BranchId ||
            budget.TotalCents != envelope.Payload.TotalCents || budget.Status != BudgetStatus.Approved)
            throw new InvalidOperationException("Pagamento não corresponde ao orçamento aprovado.");
        return await repository.RecordPaymentRequestAsync(envelope.EventId,
            new PaymentRequest(envelope.Payload.PaymentAttemptId, envelope.WorkOrderId,
                budget.Id, budget.Version, budget.BranchId, budget.TotalCents, envelope.OccurredAtUtc),
            cancellationToken);
    }
}
