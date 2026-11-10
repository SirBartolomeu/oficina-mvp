using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OficinaMvp.Infrastructure.Persistence;

namespace OficinaMvp.Infrastructure.Messaging;

public sealed class OutboxDispatcher(
    WorkshopDbContext dbContext,
    IIntegrationMessageSender sender,
    ILogger<OutboxDispatcher> logger)
{
    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        var pending = await dbContext.OutboxMessages
            .Where(item => item.PublishedAtUtc == null)
            .OrderBy(item => item.OccurredAtUtc)
            .Take(10)
            .ToListAsync(cancellationToken);
        foreach (var item in pending)
        {
            try
            {
                await sender.SendAsync(item.EventId, item.EventType, item.PayloadJson, cancellationToken);
                item.MarkPublished(DateTime.UtcNow);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                item.MarkFailed(error.Message);
                logger.LogWarning(error, "Falha ao publicar evento {EventId} da Outbox", item.EventId);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return pending.Count;
    }
}
