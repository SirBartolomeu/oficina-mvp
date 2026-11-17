namespace OficinaMvp.Application.Contracts;

public sealed record BudgetDecisionPayload(Guid BudgetId, int BudgetVersion, Guid DecisionId, string? Reason);
