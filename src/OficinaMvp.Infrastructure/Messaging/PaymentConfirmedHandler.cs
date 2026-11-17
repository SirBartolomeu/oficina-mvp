using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Domain.Exceptions;
using OficinaMvp.Infrastructure.Persistence;

namespace OficinaMvp.Infrastructure.Messaging;

public sealed class PaymentConfirmedHandler(WorkshopDbContext dbContext)
{
    public async Task<bool> HandleAsync(string body, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<IntegrationEnvelope<PaymentConfirmedPayload>>(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new DomainException("Confirmação de pagamento vazia.");
        if (envelope.EventType != "PaymentConfirmed" || envelope.SchemaVersion != 1 ||
            envelope.EventId == Guid.Empty || envelope.CorrelationId == Guid.Empty ||
            envelope.CausationId == Guid.Empty || envelope.WorkOrderId == Guid.Empty ||
            envelope.Payload is null || envelope.Payload.PaymentAttemptId == Guid.Empty ||
            string.IsNullOrWhiteSpace(envelope.Payload.ProviderOrderId) ||
            envelope.Payload.TotalCents <= 0 || envelope.Payload.Currency != "BRL")
            throw new DomainException("Contrato de confirmação de pagamento inválido.");
        if (await dbContext.InboxMessages.AnyAsync(item => item.EventId == envelope.EventId,
            cancellationToken)) return false;
        var order = await dbContext.WorkOrders.Include(item => item.StatusHistory)
            .SingleOrDefaultAsync(item => item.Id == envelope.WorkOrderId, cancellationToken)
            ?? throw new KeyNotFoundException("OS do pagamento não encontrada.");
        if (order.BranchId != envelope.BranchId ||
            order.PaymentAttemptId != envelope.Payload.PaymentAttemptId ||
            checked((long)(order.BudgetTotal * 100m)) != envelope.Payload.TotalCents ||
            !order.ReservationId.HasValue)
            throw new DomainException("Pagamento não corresponde à OS.");
        order.ConfirmPayment(envelope.OccurredAtUtc);
        var consume = new IntegrationEnvelope<ConsumeStockRequestedPayload>(
            Guid.NewGuid(), "ConsumeStockRequested", 1, envelope.CorrelationId,
            envelope.EventId, order.Id, order.BranchId, DateTime.UtcNow,
            new ConsumeStockRequestedPayload(order.ReservationId.Value));
        dbContext.OutboxMessages.Add(new OutboxMessage(consume.EventId, consume.EventType,
            JsonSerializer.Serialize(consume, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            consume.OccurredAtUtc));
        dbContext.InboxMessages.Add(new InboxMessage(envelope.EventId, envelope.EventType, DateTime.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
