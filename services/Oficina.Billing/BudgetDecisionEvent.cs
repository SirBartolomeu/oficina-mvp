namespace Oficina.Billing;

public sealed record BudgetDecisionPayload(Guid BudgetId, int BudgetVersion, Guid DecisionId, string? Reason);
public sealed record BudgetDecisionEnvelope(Guid EventId, string EventType, int SchemaVersion,
    Guid CorrelationId, Guid CausationId, Guid WorkOrderId, string? BranchId,
    DateTime OccurredAtUtc, BudgetDecisionPayload Payload);
