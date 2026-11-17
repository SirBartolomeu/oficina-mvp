using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Domain.Exceptions;
using OficinaMvp.Infrastructure.Persistence;

namespace OficinaMvp.Infrastructure.Messaging;

public sealed class StockConsumedHandler(WorkshopDbContext dbContext)
{
    public async Task<bool> HandleAsync(string body, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<IntegrationEnvelope<StockConsumedPayload>>(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new DomainException("Confirmação de consumo vazia.");
        if (envelope.EventType != "StockConsumed" || envelope.SchemaVersion != 1 ||
            envelope.EventId == Guid.Empty || envelope.CorrelationId == Guid.Empty ||
            envelope.CausationId == Guid.Empty || envelope.WorkOrderId == Guid.Empty ||
            envelope.Payload is null || envelope.Payload.ReservationId == Guid.Empty)
            throw new DomainException("Contrato de consumo inválido.");
        if (await dbContext.InboxMessages.AnyAsync(item => item.EventId == envelope.EventId,
            cancellationToken)) return false;
        var order = await dbContext.WorkOrders.Include(item => item.StatusHistory)
            .SingleOrDefaultAsync(item => item.Id == envelope.WorkOrderId, cancellationToken)
            ?? throw new KeyNotFoundException("OS do consumo não encontrada.");
        if (order.BranchId != envelope.BranchId ||
            order.ReservationId != envelope.Payload.ReservationId)
            throw new DomainException("Consumo não corresponde à OS.");
        if (order.ProcessState != WorkOrderProcessState.ReadyForExecution ||
            !order.PaymentAttemptId.HasValue)
            throw new DomainException("OS não está aguardando consumo de estoque.");
        var requested = new IntegrationEnvelope<ExecutionRequestedPayload>(Guid.NewGuid(),
            "ExecutionRequested", 1, envelope.CorrelationId, envelope.EventId,
            order.Id, order.BranchId, DateTime.UtcNow,
            new ExecutionRequestedPayload(order.Id, order.ReservationId.Value,
                order.PaymentAttemptId.Value));
        dbContext.OutboxMessages.Add(new OutboxMessage(requested.EventId,
            requested.EventType, JsonSerializer.Serialize(requested,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)), requested.OccurredAtUtc));
        dbContext.InboxMessages.Add(new InboxMessage(envelope.EventId, envelope.EventType, DateTime.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
