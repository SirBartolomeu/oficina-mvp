namespace OficinaMvp.Application.Contracts;

public sealed record ExecutionQuotedItem(Guid Id, string Name, int Quantity, long UnitPriceCents);
public sealed record ExecutionQuoteResponse(
    IReadOnlyCollection<ExecutionQuotedItem> Services,
    IReadOnlyCollection<ExecutionQuotedItem> Parts,
    long TotalCents);
