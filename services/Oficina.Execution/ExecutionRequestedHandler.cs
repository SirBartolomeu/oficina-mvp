using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oficina.Execution.Contracts;
using Oficina.Execution.Persistence;
using OficinaMvp.Domain.Exceptions;

namespace Oficina.Execution;

public sealed class ExecutionRequestedHandler(ExecutionDbContext db)
{
    public async Task<bool> HandleAsync(string body, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<IntegrationEnvelope<ExecutionRequestedPayload>>(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new DomainException("Comando de execução vazio.");
        if (envelope.EventType != "ExecutionRequested" || envelope.SchemaVersion != 1 ||
            envelope.EventId == Guid.Empty || envelope.CorrelationId == Guid.Empty ||
            envelope.CausationId == Guid.Empty || envelope.WorkOrderId == Guid.Empty ||
            envelope.Payload is null || envelope.Payload.ExecutionId != envelope.WorkOrderId ||
            envelope.Payload.ReservationId == Guid.Empty ||
            envelope.Payload.PaymentAttemptId == Guid.Empty)
            throw new DomainException("Contrato de execução inválido.");
        if (await db.InboxMessages.AnyAsync(item => item.EventId == envelope.EventId,
            cancellationToken)) return false;
        var reservation = await db.StockReservations.SingleOrDefaultAsync(item =>
            item.Id == envelope.Payload.ReservationId, cancellationToken)
            ?? throw new KeyNotFoundException("Reserva da execução não encontrada.");
        if (reservation.WorkOrderId != envelope.WorkOrderId ||
            reservation.Status != ReservationStatus.Consumed)
            throw new DomainException("Execução exige reserva consumida da mesma OS.");
        if (reservation.RepairStatus == RepairStatus.Finished &&
            reservation.PaymentAttemptId == envelope.Payload.PaymentAttemptId)
        {
            db.InboxMessages.Add(new ExecutionInboxMessage(envelope.EventId,
                envelope.EventType, DateTime.UtcNow));
            await db.SaveChangesAsync(cancellationToken);
            return false;
        }
        var occurredAt = DateTime.UtcNow;
        reservation.StartRepair(envelope.Payload.PaymentAttemptId,
            envelope.BranchId, occurredAt);
        var response = new IntegrationEnvelope<ExecutionStartedPayload>(Guid.NewGuid(),
            "ExecutionStarted", 1, envelope.CorrelationId, envelope.EventId,
            envelope.WorkOrderId, envelope.BranchId, occurredAt,
            new ExecutionStartedPayload(envelope.Payload.ExecutionId,
                envelope.Payload.ReservationId, envelope.Payload.PaymentAttemptId));
        db.OutboxMessages.Add(new ExecutionOutboxMessage(response.EventId,
            response.EventType, JsonSerializer.Serialize(response,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)), occurredAt));
        db.InboxMessages.Add(new ExecutionInboxMessage(envelope.EventId,
            envelope.EventType, occurredAt));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
