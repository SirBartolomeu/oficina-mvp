namespace OficinaMvp.Application.Contracts;

public sealed record BudgetReadyPayload(Guid BudgetId, int BudgetVersion, long TotalCents, string Currency);
