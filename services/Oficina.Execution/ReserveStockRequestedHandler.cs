using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oficina.Execution.Contracts;
using Oficina.Execution.Persistence;
using OficinaMvp.Domain.Exceptions;

namespace Oficina.Execution;

public sealed class ReserveStockRequestedHandler(ExecutionDbContext db, ExecutionService service)
{
    public async Task<bool> HandleAsync(string body, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<IntegrationEnvelope<ReserveStockRequestedPayload>>(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new DomainException("Comando de reserva vazio.");
        if (envelope.EventType != "ReserveStockRequested" || envelope.SchemaVersion != 1 ||
            envelope.EventId == Guid.Empty || envelope.WorkOrderId == Guid.Empty ||
            envelope.CorrelationId == Guid.Empty || envelope.CausationId == Guid.Empty ||
            envelope.Payload is null || envelope.Payload.ReservationId == Guid.Empty ||
            envelope.Payload.OrderVersion <= 0 || envelope.Payload.Parts is null ||
            envelope.Payload.Parts.Any(item => item.Id == Guid.Empty || item.Quantity <= 0))
            throw new DomainException("Contrato de reserva inválido.");
        if (await db.InboxMessages.AnyAsync(item => item.EventId == envelope.EventId, cancellationToken))
            return false;

        var responseType = "StockReserved";
        string? reason = null;
        try
        {
            if (DateTime.UtcNow > envelope.Payload.ExpiresAtUtc)
                throw new DomainException("Prazo da reserva expirado.");
            var result = await service.ReserveAsync(new ReserveStockRequest(
                envelope.Payload.ReservationId, envelope.WorkOrderId, envelope.Payload.Parts),
                cancellationToken, saveChanges: false);
            if (result.Status != ReservationStatus.Reserved.ToString())
            {
                responseType = "StockReservationRejected";
                reason = "Reserva já encerrada.";
            }
        }
        catch (DomainException error)
        {
            responseType = "StockReservationRejected";
            reason = error.Message;
        }

        var occurredAt = DateTime.UtcNow;
        var resultEvent = new IntegrationEnvelope<StockReservationResultPayload>(
            Guid.NewGuid(), responseType, 1, envelope.CorrelationId, envelope.EventId,
            envelope.WorkOrderId, envelope.BranchId, occurredAt,
            new StockReservationResultPayload(envelope.Payload.ReservationId,
                envelope.Payload.OrderVersion, reason));
        db.OutboxMessages.Add(new ExecutionOutboxMessage(resultEvent.EventId, responseType,
            JsonSerializer.Serialize(resultEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web)), occurredAt));
        db.InboxMessages.Add(new ExecutionInboxMessage(envelope.EventId, envelope.EventType, occurredAt));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
