using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Domain.Exceptions;
using OficinaMvp.Infrastructure.Persistence;

namespace OficinaMvp.Infrastructure.Messaging;

public sealed class StockReleasedHandler(WorkshopDbContext dbContext)
{
    public async Task<bool> HandleAsync(string body, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<IntegrationEnvelope<StockReleasedPayload>>(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new DomainException("Confirmação de liberação vazia.");
        if (envelope.EventType != "StockReleased" || envelope.SchemaVersion != 1 ||
            envelope.EventId == Guid.Empty || envelope.CorrelationId == Guid.Empty ||
            envelope.CausationId == Guid.Empty || envelope.WorkOrderId == Guid.Empty ||
            envelope.Payload is null || envelope.Payload.ReservationId == Guid.Empty)
            throw new DomainException("Contrato de liberação inválido.");
        if (await dbContext.InboxMessages.AnyAsync(item => item.EventId == envelope.EventId,
            cancellationToken)) return false;
        var order = await dbContext.WorkOrders.Include(item => item.StatusHistory)
            .SingleOrDefaultAsync(item => item.Id == envelope.WorkOrderId, cancellationToken)
            ?? throw new KeyNotFoundException("OS da liberação não encontrada.");
        if (order.BranchId != envelope.BranchId ||
            order.ReservationId != envelope.Payload.ReservationId)
            throw new DomainException("Liberação não corresponde à OS.");
        if (order.ProcessState == WorkOrderProcessState.ReleasingStock)
            order.ConfirmStockRelease(envelope.OccurredAtUtc);
        else if (order.ProcessState != WorkOrderProcessState.Canceled)
            throw new DomainException("OS não está aguardando liberação de estoque.");
        dbContext.InboxMessages.Add(new InboxMessage(envelope.EventId, envelope.EventType, DateTime.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
