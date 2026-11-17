using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Domain.Exceptions;
using OficinaMvp.Infrastructure.Persistence;

namespace OficinaMvp.Infrastructure.Messaging;

public sealed class RefundConfirmedHandler(WorkshopDbContext dbContext)
{
    public async Task<bool> HandleAsync(string body, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<IntegrationEnvelope<RefundConfirmedPayload>>(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new DomainException("Confirmação de estorno vazia.");
        if (envelope.EventType != "RefundConfirmed" || envelope.SchemaVersion != 1 ||
            envelope.EventId == Guid.Empty || envelope.CorrelationId == Guid.Empty ||
            envelope.CausationId == Guid.Empty || envelope.WorkOrderId == Guid.Empty ||
            envelope.Payload is null || envelope.Payload.PaymentAttemptId == Guid.Empty ||
            envelope.Payload.RefundRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(envelope.Payload.ProviderOrderId) ||
            envelope.Payload.TotalCents <= 0 || envelope.Payload.Currency != "BRL")
            throw new DomainException("Contrato de confirmação de estorno inválido.");
        if (await dbContext.InboxMessages.AnyAsync(item => item.EventId == envelope.EventId,
            cancellationToken)) return false;
        var order = await dbContext.WorkOrders.Include(item => item.StatusHistory)
            .SingleOrDefaultAsync(item => item.Id == envelope.WorkOrderId, cancellationToken)
            ?? throw new KeyNotFoundException("OS do estorno não encontrada.");
        if (order.BranchId != envelope.BranchId ||
            order.PaymentAttemptId != envelope.Payload.PaymentAttemptId ||
            order.RefundRequestId != envelope.Payload.RefundRequestId ||
            checked((long)(order.BudgetTotal * 100m)) != envelope.Payload.TotalCents)
            throw new DomainException("Estorno não corresponde à OS.");
        if (order.ProcessState == WorkOrderProcessState.RefundRequested)
            order.ConfirmRefund(envelope.OccurredAtUtc);
        else if (order.ProcessState != WorkOrderProcessState.Canceled ||
                 order.Status != WorkOrderStatus.Canceled)
            throw new DomainException("OS não aguarda confirmação de estorno.");
        dbContext.InboxMessages.Add(new InboxMessage(envelope.EventId,
            envelope.EventType, DateTime.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
