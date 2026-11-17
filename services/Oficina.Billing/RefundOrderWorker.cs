namespace Oficina.Billing;

public sealed class RefundOrderProcessor(BillingRepository repository,
    MercadoPagoQrClient mercadoPago, ILogger<RefundOrderProcessor> logger)
{
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var completed = 0;
        foreach (var paymentId in await repository.GetPendingRefundsAsync(cancellationToken))
        {
            try
            {
                var payment = await repository.GetPaymentRequestAsync(paymentId, cancellationToken)
                    ?? throw new InvalidOperationException("Estorno pendente sem pagamento.");
                var attempt = await repository.GetPaymentAttemptAsync(paymentId, cancellationToken)
                    ?? throw new InvalidOperationException("Estorno pendente sem tentativa.");
                if (attempt.Status is not "RefundRequested" and not "RefundSubmitting" ||
                    !attempt.RefundRequestId.HasValue ||
                    string.IsNullOrWhiteSpace(attempt.ProviderOrderId))
                    throw new InvalidOperationException("Estado do estorno pendente inválido.");
                var order = await mercadoPago.GetAsync(attempt.ProviderOrderId,
                    cancellationToken);
                if (order.ExternalReference != $"pay_{payment.Id:N}" ||
                    order.Currency != "BRL" || order.TotalCents != payment.TotalCents)
                    throw new InvalidOperationException("Order do estorno não corresponde ao pagamento.");
                if (order.Status == "refunded" && order.PaymentStatus == "refunded")
                {
                    if (attempt.Status == "RefundRequested")
                        await repository.BeginRefundSubmissionAsync(paymentId,
                            attempt.RefundRequestId.Value, cancellationToken);
                    if (await repository.ConfirmRefundAsync(payment,
                        attempt.RefundRequestId.Value, order.Id, cancellationToken)) completed++;
                    continue;
                }
                if (order.Status != "processed" || order.PaymentStatus != "processed" ||
                    order.PaidCents != payment.TotalCents)
                    throw new InvalidOperationException("Order não está apta ao estorno total.");
                if (attempt.Status == "RefundSubmitting")
                {
                    logger.LogWarning("Estorno {RefundRequestId} aguarda confirmação do provedor",
                        attempt.RefundRequestId);
                    continue;
                }
                if (!await repository.BeginRefundSubmissionAsync(paymentId,
                    attempt.RefundRequestId.Value, cancellationToken)) continue;
                await mercadoPago.RequestRefundAsync(order.Id, attempt.RefundRequestId.Value,
                    cancellationToken);
                var reconciled = await mercadoPago.GetAsync(order.Id, cancellationToken);
                if (reconciled.ExternalReference == $"pay_{payment.Id:N}" &&
                    reconciled.Currency == "BRL" && reconciled.TotalCents == payment.TotalCents &&
                    reconciled.Status == "refunded" && reconciled.PaymentStatus == "refunded" &&
                    await repository.ConfirmRefundAsync(payment, attempt.RefundRequestId.Value,
                        reconciled.Id, cancellationToken)) completed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                logger.LogWarning(error, "Estorno {PaymentAttemptId} requer reconciliação",
                    paymentId);
            }
        }
        return completed;
    }
}

public sealed class RefundOrderWorker(IServiceScopeFactory scopes,
    ILogger<RefundOrderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<RefundOrderProcessor>()
                    .ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogError(error, "Falha ao reconciliar estornos"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
