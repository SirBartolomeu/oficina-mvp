using OficinaMvp.Infrastructure.Messaging;

namespace OficinaMvp.Api.Messaging;

public sealed class OutboxBackgroundService(IServiceScopeFactory scopes, ILogger<OutboxBackgroundService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>()
                    .DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Falha ao processar Outbox da OS");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
