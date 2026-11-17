namespace OficinaMvp.Application.Contracts;

public sealed record StockItem(Guid Id, int Quantity);
public sealed record ReserveStockRequestedPayload(
    Guid ReservationId,
    int OrderVersion,
    IReadOnlyCollection<StockItem> Parts,
    DateTime ExpiresAtUtc);
public sealed record StockReservationResultPayload(
    Guid ReservationId,
    int OrderVersion,
    string? Reason);
