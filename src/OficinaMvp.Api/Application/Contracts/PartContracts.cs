namespace OficinaMvp.Api.Application.Contracts;

public sealed record UpsertPartSupplyRequest(string Name, decimal UnitPrice, int StockQuantity);
public sealed record PartSupplyResponse(Guid Id, string Name, decimal UnitPrice, int StockQuantity);
