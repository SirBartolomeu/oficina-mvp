using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Domain.Exceptions;
using OficinaMvp.Infrastructure.Persistence;

namespace OficinaMvp.Infrastructure.Messaging;

public sealed class StockReservationResultHandler(WorkshopDbContext dbContext)
{
    public async Task<bool> HandleAsync(string body, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<IntegrationEnvelope<StockReservationResultPayload>>(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new DomainException("Resultado da reserva vazio.");
        if (envelope.EventType is not "StockReserved" and not "StockReservationRejected" ||
            envelope.SchemaVersion != 1 || envelope.EventId == Guid.Empty ||
            envelope.CorrelationId == Guid.Empty || envelope.CausationId == Guid.Empty ||
            envelope.WorkOrderId == Guid.Empty || envelope.Payload is null ||
            envelope.Payload.ReservationId == Guid.Empty || envelope.Payload.OrderVersion <= 0)
            throw new DomainException("Contrato de resultado da reserva inválido.");
        if (await dbContext.InboxMessages.AnyAsync(item => item.EventId == envelope.EventId, cancellationToken))
            return false;

        var order = await dbContext.WorkOrders.Include(item => item.StatusHistory)
            .SingleOrDefaultAsync(item => item.Id == envelope.WorkOrderId, cancellationToken)
            ?? throw new KeyNotFoundException("OS da reserva não encontrada.");
        if (order.BranchId != envelope.BranchId || order.ReservationId != envelope.Payload.ReservationId ||
            order.BudgetVersion != envelope.Payload.OrderVersion)
            throw new DomainException("Resultado da reserva não corresponde à OS.");
        if (envelope.EventType == "StockReserved")
        {
            var paymentAttemptId = Guid.NewGuid();
            order.ConfirmStockReservation(envelope.OccurredAtUtc, paymentAttemptId);
            var payment = new IntegrationEnvelope<CreatePaymentRequestedPayload>(
                Guid.NewGuid(), "CreatePaymentRequested", 1, envelope.CorrelationId,
                envelope.EventId, order.Id, order.BranchId, DateTime.UtcNow,
                new CreatePaymentRequestedPayload(paymentAttemptId, order.BudgetId!.Value,
                    order.BudgetVersion!.Value, checked((long)(order.BudgetTotal * 100m)), "BRL"));
            dbContext.OutboxMessages.Add(new OutboxMessage(payment.EventId, payment.EventType,
                JsonSerializer.Serialize(payment, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                payment.OccurredAtUtc));
        }
        else
        {
            var reason = envelope.Payload.Reason ?? "Estoque indisponível.";
            order.RejectStockReservation(envelope.OccurredAtUtc, reason[..Math.Min(180, reason.Length)]);
        }

        dbContext.InboxMessages.Add(new InboxMessage(envelope.EventId, envelope.EventType, DateTime.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
