using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oficina.Execution.Contracts;
using Oficina.Execution.Persistence;
using OficinaMvp.Domain.Exceptions;

namespace Oficina.Execution;

public sealed class ConsumeStockRequestedHandler(ExecutionDbContext db, ExecutionService service)
{
    public async Task<bool> HandleAsync(string body, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<IntegrationEnvelope<ConsumeStockRequestedPayload>>(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new DomainException("Comando de consumo vazio.");
        if (envelope.EventType != "ConsumeStockRequested" || envelope.SchemaVersion != 1 ||
            envelope.EventId == Guid.Empty || envelope.CorrelationId == Guid.Empty ||
            envelope.CausationId == Guid.Empty || envelope.WorkOrderId == Guid.Empty ||
            envelope.Payload is null || envelope.Payload.ReservationId == Guid.Empty)
            throw new DomainException("Contrato de consumo inválido.");
        if (await db.InboxMessages.AnyAsync(item => item.EventId == envelope.EventId,
            cancellationToken)) return false;
        var result = await service.ConsumeAsync(envelope.Payload.ReservationId,
            cancellationToken, saveChanges: false)
            ?? throw new KeyNotFoundException("Reserva para consumo não encontrada.");
        if (result.WorkOrderId != envelope.WorkOrderId ||
            result.Status != ReservationStatus.Consumed.ToString())
            throw new DomainException("Reserva não corresponde à OS ou não foi consumida.");
        var occurredAt = DateTime.UtcNow;
        var response = new IntegrationEnvelope<StockConsumedPayload>(Guid.NewGuid(),
            "StockConsumed", 1, envelope.CorrelationId, envelope.EventId,
            envelope.WorkOrderId, envelope.BranchId, occurredAt,
            new StockConsumedPayload(envelope.Payload.ReservationId));
        db.OutboxMessages.Add(new ExecutionOutboxMessage(response.EventId, response.EventType,
            JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            occurredAt));
        db.InboxMessages.Add(new ExecutionInboxMessage(envelope.EventId, envelope.EventType, occurredAt));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
