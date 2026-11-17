namespace Oficina.Execution.Contracts;

public sealed record CatalogItemRequest(Guid Id, int Quantity);
public sealed record QuoteRequest(IReadOnlyCollection<CatalogItemRequest> Services, IReadOnlyCollection<CatalogItemRequest> Parts);
public sealed record QuotedItem(Guid Id, string Name, int Quantity, long UnitPriceCents);
public sealed record QuoteResponse(IReadOnlyCollection<QuotedItem> Services, IReadOnlyCollection<QuotedItem> Parts, long TotalCents);
public sealed record ReserveStockRequest(Guid ReservationId, Guid WorkOrderId, IReadOnlyCollection<CatalogItemRequest> Parts);
public sealed record ReservationResponse(Guid ReservationId, Guid WorkOrderId, string Status);
