namespace Oficina.Billing;

public sealed class PaymentOrderProcessor(BillingRepository repository, MercadoPagoQrClient mercadoPago,
    ILogger<PaymentOrderProcessor> logger)
{
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var completed = 0;
        foreach (var paymentId in await repository.GetPendingPaymentsAsync(cancellationToken))
        {
            try
            {
                var payment = await repository.GetPaymentRequestAsync(paymentId, cancellationToken)
                    ?? throw new InvalidOperationException("Tentativa pendente sem registro.");
                if (DateTime.UtcNow - payment.RequestedAtUtc > TimeSpan.FromHours(23))
                {
                    await repository.MarkPaymentManualReviewAsync(paymentId, cancellationToken);
                    logger.LogWarning("Tentativa {PaymentAttemptId} requer revisão antes de criar nova order",
                        paymentId);
                    continue;
                }
                var order = await mercadoPago.CreateAsync(payment, cancellationToken);
                if (await repository.CompletePaymentCreationAsync(paymentId, order, cancellationToken))
                    completed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                logger.LogWarning(error, "Falha ao criar order da tentativa {PaymentAttemptId}", paymentId);
            }
        }
        return completed;
    }
}

public sealed class PaymentOrderWorker(IServiceScopeFactory scopes,
    ILogger<PaymentOrderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<PaymentOrderProcessor>()
                    .ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogError(error, "Falha ao processar tentativas de pagamento"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
