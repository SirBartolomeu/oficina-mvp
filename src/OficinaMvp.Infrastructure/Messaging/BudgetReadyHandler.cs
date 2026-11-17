using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Domain.Exceptions;
using OficinaMvp.Infrastructure.Persistence;

namespace OficinaMvp.Infrastructure.Messaging;

public sealed class BudgetReadyHandler(WorkshopDbContext dbContext)
{
    public async Task<bool> HandleAsync(string messageBody, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<IntegrationEnvelope<BudgetReadyPayload>>(messageBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new DomainException("Evento BudgetReady vazio.");
        if (envelope.EventType != "BudgetReady" || envelope.SchemaVersion != 1 ||
            envelope.EventId == Guid.Empty || envelope.WorkOrderId == Guid.Empty ||
            envelope.CorrelationId == Guid.Empty || envelope.CausationId == Guid.Empty ||
            envelope.Payload is null || envelope.Payload.Currency != "BRL" ||
            envelope.Payload.BudgetId == Guid.Empty || envelope.Payload.BudgetVersion <= 0)
            throw new DomainException("Contrato BudgetReady inválido.");

        if (await dbContext.InboxMessages.AnyAsync(item => item.EventId == envelope.EventId, cancellationToken))
            return false;

        var order = await dbContext.WorkOrders.Include(item => item.StatusHistory)
            .SingleOrDefaultAsync(item => item.Id == envelope.WorkOrderId, cancellationToken)
            ?? throw new KeyNotFoundException("OS do evento BudgetReady não encontrada.");
        if (order.BranchId != envelope.BranchId ||
            checked((long)(order.BudgetTotal * 100m)) != envelope.Payload.TotalCents)
            throw new DomainException("BudgetReady não corresponde ao orçamento da OS.");

        order.AcceptBudgetReady(envelope.Payload.BudgetId, envelope.Payload.BudgetVersion,
            envelope.OccurredAtUtc);
        dbContext.InboxMessages.Add(new InboxMessage(envelope.EventId, envelope.EventType, DateTime.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
