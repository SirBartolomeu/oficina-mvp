using System.Text.Json;

namespace Oficina.Billing;

public sealed record WorkOrderOpenedLine(string Kind, Guid ItemId, string Name, int Quantity, long UnitPriceCents);
public sealed record WorkOrderOpenedPayload(int OrderVersion, IReadOnlyCollection<WorkOrderOpenedLine> Lines,
    long TotalCents, string Currency, DateTime QuotedAtUtc);
public sealed record WorkOrderOpenedEnvelope(Guid EventId, string EventType, int SchemaVersion,
    Guid CorrelationId, Guid CausationId, Guid WorkOrderId, string? BranchId,
    DateTime OccurredAtUtc, WorkOrderOpenedPayload Payload);

public sealed class WorkOrderOpenedHandler(IBudgetEventStore repository)
{
    public async Task<Budget> HandleAsync(string messageBody, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<WorkOrderOpenedEnvelope>(messageBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new ArgumentException("Evento WorkOrderOpened vazio.");
        if (envelope.EventType != "WorkOrderOpened" || envelope.SchemaVersion != 1 ||
            envelope.EventId == Guid.Empty || envelope.WorkOrderId == Guid.Empty ||
            envelope.CorrelationId == Guid.Empty || envelope.Payload is null ||
            envelope.Payload.Currency != "BRL" || envelope.Payload.Lines is null)
            throw new ArgumentException("Contrato WorkOrderOpened inválido.");

        var budget = Budget.Create(envelope.WorkOrderId, envelope.Payload.OrderVersion,
            envelope.BranchId, envelope.Payload.Lines.Select(line =>
                new BudgetLine(line.ItemId, line.Name, line.Quantity, line.UnitPriceCents)).ToArray());
        if (budget.TotalCents != envelope.Payload.TotalCents)
            throw new ArgumentException("Total do evento não corresponde às linhas.");
        return await repository.CreateFromEventAsync(envelope.EventId, budget, cancellationToken);
    }
}
