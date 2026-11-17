using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Domain.Exceptions;
using OficinaMvp.Infrastructure.Persistence;

namespace OficinaMvp.Infrastructure.Messaging;

public sealed class BudgetDecisionHandler(WorkshopDbContext dbContext)
{
    public async Task<bool> HandleAsync(string body, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<IntegrationEnvelope<BudgetDecisionPayload>>(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new DomainException("Decisão do orçamento vazia.");
        if (envelope.EventType is not "BudgetApproved" and not "BudgetRefused" ||
            envelope.SchemaVersion != 1 || envelope.EventId == Guid.Empty ||
            envelope.CorrelationId == Guid.Empty || envelope.CausationId == Guid.Empty ||
            envelope.WorkOrderId == Guid.Empty || envelope.Payload is null ||
            envelope.Payload.BudgetId == Guid.Empty || envelope.Payload.BudgetVersion <= 0 ||
            envelope.Payload.DecisionId != envelope.EventId)
            throw new DomainException("Contrato de decisão do orçamento inválido.");
        if (await dbContext.InboxMessages.AnyAsync(item => item.EventId == envelope.EventId, cancellationToken))
            return false;

        var order = await dbContext.WorkOrders.Include(item => item.Parts)
            .Include(item => item.StatusHistory)
            .SingleOrDefaultAsync(item => item.Id == envelope.WorkOrderId, cancellationToken)
            ?? throw new KeyNotFoundException("OS da decisão não encontrada.");
        if (order.BranchId != envelope.BranchId || order.BudgetId != envelope.Payload.BudgetId ||
            order.BudgetVersion != envelope.Payload.BudgetVersion)
            throw new DomainException("Decisão não corresponde ao orçamento corrente da OS.");

        if (envelope.EventType == "BudgetApproved")
        {
            var reservationId = Guid.NewGuid();
            order.ApproveBudgetForPayment(envelope.OccurredAtUtc, reservationId);
            var command = new IntegrationEnvelope<ReserveStockRequestedPayload>(
                Guid.NewGuid(), "ReserveStockRequested", 1, envelope.CorrelationId, envelope.EventId,
                order.Id, order.BranchId, DateTime.UtcNow,
                new ReserveStockRequestedPayload(reservationId, order.BudgetVersion!.Value,
                    order.Parts.Select(item => new StockItem(item.PartSupplyId, item.Quantity)).ToArray(),
                    DateTime.UtcNow.AddMinutes(15)));
            dbContext.OutboxMessages.Add(new OutboxMessage(command.EventId, command.EventType,
                JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                command.OccurredAtUtc));
        }
        else
            order.RegisterBudgetRefusal(envelope.OccurredAtUtc, envelope.Payload.Reason);

        dbContext.InboxMessages.Add(new InboxMessage(envelope.EventId, envelope.EventType, DateTime.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
