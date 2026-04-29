using OficinaMvp.Api.Domain.Entities;

namespace OficinaMvp.Api.Application.Contracts;

public sealed record CreateWorkOrderRequest
{
    public string CustomerDocument { get; init; } = string.Empty;
    public VehicleInfo Vehicle { get; init; } = new();
    public IReadOnlyCollection<RequestedService> Services { get; init; } = Array.Empty<RequestedService>();
    public IReadOnlyCollection<RequestedPart> Parts { get; init; } = Array.Empty<RequestedPart>();
    public string? Notes { get; init; }
}

public sealed record VehicleInfo
{
    public string LicensePlate { get; init; } = string.Empty;
    public string Brand { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public int Year { get; init; }
}

public sealed record RequestedService
{
    public Guid ServiceId { get; init; }
    public int Quantity { get; init; }
}

public sealed record RequestedPart
{
    public Guid PartId { get; init; }
    public int Quantity { get; init; }
}

public enum WorkOrderAction
{
    StartDiagnosis = 1,
    SendBudget = 2,
    ApproveBudget = 3,
    Finalize = 4,
    Deliver = 5
}

public sealed record WorkOrderSummaryResponse(
    Guid Id,
    WorkOrderStatus Status,
    decimal BudgetTotal,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record WorkOrderServiceLineResponse(Guid ServiceId, string ServiceName, int Quantity, decimal UnitPrice, decimal Subtotal);
public sealed record WorkOrderPartLineResponse(Guid PartSupplyId, string PartName, int Quantity, decimal UnitPrice, decimal Subtotal);
public sealed record WorkOrderStatusHistoryResponse(WorkOrderStatus Status, DateTime ChangedAtUtc, string Note);

public sealed record WorkOrderDetailResponse(
    Guid Id,
    Guid CustomerId,
    Guid VehicleId,
    WorkOrderStatus Status,
    decimal ServicesTotal,
    decimal PartsTotal,
    decimal BudgetTotal,
    string? Notes,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyCollection<WorkOrderServiceLineResponse> Services,
    IReadOnlyCollection<WorkOrderPartLineResponse> Parts,
    IReadOnlyCollection<WorkOrderStatusHistoryResponse> StatusHistory);

public sealed record AverageExecutionTimeResponse(double AverageMinutes, int ProcessedOrders);

public sealed record ClientTrackingResponse(
    Guid WorkOrderId,
    string CustomerDocument,
    WorkOrderStatus Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    decimal BudgetTotal,
    IReadOnlyCollection<WorkOrderStatusHistoryResponse> StatusHistory);
