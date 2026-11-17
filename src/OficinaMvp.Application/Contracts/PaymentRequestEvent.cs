namespace OficinaMvp.Application.Contracts;

public sealed record CreatePaymentRequestedPayload(
    Guid PaymentAttemptId, Guid BudgetId, int BudgetVersion, long TotalCents, string Currency);
