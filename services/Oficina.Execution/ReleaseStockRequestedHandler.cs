using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oficina.Execution.Contracts;
using Oficina.Execution.Persistence;
using OficinaMvp.Domain.Exceptions;

namespace Oficina.Execution;

public sealed class ReleaseStockRequestedHandler(ExecutionDbContext db, ExecutionService service)
{
    public async Task<bool> HandleAsync(string body, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<IntegrationEnvelope<ReleaseStockRequestedPayload>>(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new DomainException("Comando de liberação vazio.");
        if (envelope.EventType != "ReleaseStockRequested" || envelope.SchemaVersion != 1 ||
            envelope.EventId == Guid.Empty || envelope.CorrelationId == Guid.Empty ||
            envelope.CausationId == Guid.Empty || envelope.WorkOrderId == Guid.Empty ||
            envelope.Payload is null || envelope.Payload.ReservationId == Guid.Empty ||
            envelope.Payload.Reason is not "expired" and not "canceled" and not "failed")
            throw new DomainException("Contrato de liberação inválido.");
        if (await db.InboxMessages.AnyAsync(item => item.EventId == envelope.EventId,
            cancellationToken)) return false;
        var reservation = await db.StockReservations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == envelope.Payload.ReservationId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Reserva para liberação não encontrada.");
        if (reservation.WorkOrderId != envelope.WorkOrderId)
            throw new DomainException("Reserva não corresponde à OS.");
        var result = await service.ReleaseAsync(envelope.Payload.ReservationId,
            cancellationToken, saveChanges: false)
            ?? throw new KeyNotFoundException("Reserva para liberação não encontrada.");
        if (result.Status != ReservationStatus.Released.ToString())
            throw new DomainException("Reserva não foi liberada.");
        var occurredAt = DateTime.UtcNow;
        var response = new IntegrationEnvelope<StockReleasedPayload>(Guid.NewGuid(),
            "StockReleased", 1, envelope.CorrelationId, envelope.EventId,
            envelope.WorkOrderId, envelope.BranchId, occurredAt,
            new StockReleasedPayload(envelope.Payload.ReservationId));
        db.OutboxMessages.Add(new ExecutionOutboxMessage(response.EventId, response.EventType,
            JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            occurredAt));
        db.InboxMessages.Add(new ExecutionInboxMessage(envelope.EventId, envelope.EventType, occurredAt));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
