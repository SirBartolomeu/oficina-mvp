using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Domain.Exceptions;
using OficinaMvp.Infrastructure.Persistence;

namespace OficinaMvp.Infrastructure.Messaging;

public sealed class PaymentRejectedHandler(WorkshopDbContext dbContext)
{
    public async Task<bool> HandleAsync(string body, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<IntegrationEnvelope<PaymentRejectedPayload>>(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new DomainException("Recusa de pagamento vazia.");
        if (envelope.EventType != "PaymentRejected" || envelope.SchemaVersion != 1 ||
            envelope.EventId == Guid.Empty || envelope.CorrelationId == Guid.Empty ||
            envelope.CausationId == Guid.Empty || envelope.WorkOrderId == Guid.Empty ||
            envelope.Payload is null || envelope.Payload.PaymentAttemptId == Guid.Empty ||
            string.IsNullOrWhiteSpace(envelope.Payload.ProviderOrderId) ||
            envelope.Payload.TotalCents <= 0 || envelope.Payload.Currency != "BRL" ||
            envelope.Payload.Reason is not "expired" and not "canceled" and not "failed")
            throw new DomainException("Contrato de recusa de pagamento inválido.");
        if (await dbContext.InboxMessages.AnyAsync(item => item.EventId == envelope.EventId,
            cancellationToken)) return false;
        var order = await dbContext.WorkOrders.Include(item => item.StatusHistory)
            .SingleOrDefaultAsync(item => item.Id == envelope.WorkOrderId, cancellationToken)
            ?? throw new KeyNotFoundException("OS do pagamento não encontrada.");
        if (order.BranchId != envelope.BranchId ||
            order.PaymentAttemptId != envelope.Payload.PaymentAttemptId ||
            checked((long)(order.BudgetTotal * 100m)) != envelope.Payload.TotalCents ||
            !order.ReservationId.HasValue)
            throw new DomainException("Recusa de pagamento não corresponde à OS.");
        order.BeginStockRelease(envelope.OccurredAtUtc, envelope.Payload.Reason);
        var release = new IntegrationEnvelope<ReleaseStockRequestedPayload>(
            Guid.NewGuid(), "ReleaseStockRequested", 1, envelope.CorrelationId,
            envelope.EventId, order.Id, order.BranchId, DateTime.UtcNow,
            new ReleaseStockRequestedPayload(order.ReservationId.Value, envelope.Payload.Reason));
        dbContext.OutboxMessages.Add(new OutboxMessage(release.EventId, release.EventType,
            JsonSerializer.Serialize(release, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            release.OccurredAtUtc));
        dbContext.InboxMessages.Add(new InboxMessage(envelope.EventId, envelope.EventType, DateTime.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
