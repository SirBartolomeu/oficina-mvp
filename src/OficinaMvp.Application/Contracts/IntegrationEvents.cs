namespace OficinaMvp.Application.Contracts;

public sealed record IntegrationEnvelope<TPayload>(
    Guid EventId,
    string EventType,
    int SchemaVersion,
    Guid CorrelationId,
    Guid CausationId,
    Guid WorkOrderId,
    string? BranchId,
    DateTime OccurredAtUtc,
    TPayload Payload);

public sealed record WorkOrderOpenedLine(
    string Kind,
    Guid ItemId,
    string Name,
    int Quantity,
    long UnitPriceCents);

public sealed record WorkOrderOpenedPayload(
    int OrderVersion,
    IReadOnlyCollection<WorkOrderOpenedLine> Lines,
    long TotalCents,
    string Currency,
    DateTime QuotedAtUtc);
