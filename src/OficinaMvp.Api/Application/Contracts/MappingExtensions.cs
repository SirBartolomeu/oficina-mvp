using OficinaMvp.Api.Domain.Entities;

namespace OficinaMvp.Api.Application.Contracts;

public static class MappingExtensions
{
    public static CustomerResponse ToResponse(this Customer customer) =>
        new(customer.Id, customer.Name, customer.Document, customer.Phone, customer.Email);

    public static VehicleResponse ToResponse(this Vehicle vehicle) =>
        new(vehicle.Id, vehicle.CustomerId, vehicle.LicensePlate, vehicle.Brand, vehicle.Model, vehicle.Year);

    public static RepairServiceResponse ToResponse(this RepairService service) =>
        new(service.Id, service.Name, service.Description, service.LaborPrice, service.AverageDurationMinutes);

    public static PartSupplyResponse ToResponse(this PartSupply part) =>
        new(part.Id, part.Name, part.UnitPrice, part.StockQuantity);

    public static WorkOrderSummaryResponse ToSummaryResponse(this WorkOrder workOrder) =>
        new(workOrder.Id, workOrder.Status, workOrder.BudgetTotal, workOrder.CreatedAtUtc, workOrder.UpdatedAtUtc);

    public static WorkOrderDetailResponse ToDetailResponse(this WorkOrder workOrder) =>
        new(
            workOrder.Id,
            workOrder.CustomerId,
            workOrder.VehicleId,
            workOrder.Status,
            workOrder.ServicesTotal,
            workOrder.PartsTotal,
            workOrder.BudgetTotal,
            workOrder.Notes,
            workOrder.CreatedAtUtc,
            workOrder.UpdatedAtUtc,
            workOrder.Services
                .Select(service => new WorkOrderServiceLineResponse(
                    service.ServiceId,
                    service.ServiceName,
                    service.Quantity,
                    service.UnitPrice,
                    service.Subtotal))
                .ToList(),
            workOrder.Parts
                .Select(part => new WorkOrderPartLineResponse(
                    part.PartSupplyId,
                    part.PartName,
                    part.Quantity,
                    part.UnitPrice,
                    part.Subtotal))
                .ToList(),
            workOrder.StatusHistory
                .OrderBy(history => history.ChangedAtUtc)
                .Select(history => new WorkOrderStatusHistoryResponse(
                    history.Status,
                    history.ChangedAtUtc,
                    history.Note))
                .ToList());

    public static ClientTrackingResponse ToClientTrackingResponse(this WorkOrder workOrder, string customerDocument) =>
        new(
            workOrder.Id,
            customerDocument,
            workOrder.Status,
            workOrder.CreatedAtUtc,
            workOrder.UpdatedAtUtc,
            workOrder.BudgetTotal,
            workOrder.StatusHistory
                .OrderBy(history => history.ChangedAtUtc)
                .Select(history => new WorkOrderStatusHistoryResponse(
                    history.Status,
                    history.ChangedAtUtc,
                    history.Note))
                .ToList());
}
