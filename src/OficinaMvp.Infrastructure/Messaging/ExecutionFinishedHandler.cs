using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Domain.Exceptions;
using OficinaMvp.Infrastructure.Persistence;

namespace OficinaMvp.Infrastructure.Messaging;

public sealed class ExecutionFinishedHandler(WorkshopDbContext dbContext)
{
    public async Task<bool> HandleAsync(string body, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<IntegrationEnvelope<ExecutionFinishedPayload>>(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new DomainException("Conclusão de execução vazia.");
        if (envelope.EventType != "ExecutionFinished" || envelope.SchemaVersion != 1 ||
            envelope.EventId == Guid.Empty || envelope.CorrelationId == Guid.Empty ||
            envelope.CausationId == Guid.Empty || envelope.WorkOrderId == Guid.Empty ||
            envelope.Payload is null || envelope.Payload.ExecutionId != envelope.WorkOrderId ||
            envelope.Payload.ReservationId == Guid.Empty ||
            envelope.Payload.PaymentAttemptId == Guid.Empty)
            throw new DomainException("Contrato de conclusão inválido.");
        if (await dbContext.InboxMessages.AnyAsync(item => item.EventId == envelope.EventId,
            cancellationToken)) return false;
        var order = await dbContext.WorkOrders.Include(item => item.StatusHistory)
            .SingleOrDefaultAsync(item => item.Id == envelope.WorkOrderId, cancellationToken)
            ?? throw new KeyNotFoundException("OS da conclusão não encontrada.");
        if (order.BranchId != envelope.BranchId ||
            order.ReservationId != envelope.Payload.ReservationId ||
            order.PaymentAttemptId != envelope.Payload.PaymentAttemptId)
            throw new DomainException("Conclusão não corresponde à OS.");
        if (order.ProcessState == WorkOrderProcessState.InExecution)
            order.Finalize(envelope.OccurredAtUtc);
        else if (order.ProcessState != WorkOrderProcessState.Completed &&
                 order.Status != WorkOrderStatus.Delivered)
            throw new DomainException("OS não está em execução.");
        dbContext.InboxMessages.Add(new InboxMessage(envelope.EventId,
            envelope.EventType, DateTime.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
