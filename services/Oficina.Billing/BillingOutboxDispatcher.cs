using Microsoft.Extensions.Logging;

namespace Oficina.Billing;

public sealed class BillingOutboxDispatcher(BillingRepository repository,
    Func<BillingOutboxMessage, CancellationToken, Task> send,
    ILogger logger, TimeProvider? timeProvider = null)
{
    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var pending = await repository.GetPendingOutboxAsync(cancellationToken);
        var published = 0;
        foreach (var item in pending)
        {
            var claimId = await repository.TryClaimOutboxAsync(item.EventId,
                clock.GetUtcNow(), cancellationToken);
            if (claimId is null) continue;

            var sent = false;
            try
            {
                await send(item, cancellationToken);
                sent = true;
                await repository.MarkClaimedOutboxPublishedAsync(item.EventId,
                    claimId.Value, cancellationToken);
                published++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "Falha ao publicar evento {EventId} do Billing", item.EventId);
                if (sent) continue;
                try
                {
                    await repository.ReleaseOutboxClaimAsync(item.EventId, claimId.Value,
                        cancellationToken);
                }
                catch (Exception releaseError)
                {
                    logger.LogWarning(releaseError,
                        "Falha ao liberar reivindicação do evento {EventId} do Billing", item.EventId);
                }
            }
        }
        return published;
    }
}
