using Microsoft.EntityFrameworkCore;
using OficinaMvp.Api.Application.Contracts;
using OficinaMvp.Api.Domain.Entities;
using OficinaMvp.Api.Domain.Exceptions;
using OficinaMvp.Api.Domain.Validation;
using OficinaMvp.Api.Infrastructure.Persistence;

namespace OficinaMvp.Api.Application.Services;

public sealed class WorkOrderApplicationService
{
    private readonly WorkshopDbContext _dbContext;

    public WorkOrderApplicationService(WorkshopDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<WorkOrder> CreateAsync(CreateWorkOrderRequest request, CancellationToken cancellationToken)
    {
        var requestedServices = request.Services ?? Array.Empty<RequestedService>();
        var requestedParts = request.Parts ?? Array.Empty<RequestedPart>();

        var hasServices = requestedServices.Count > 0;
        var hasParts = requestedParts.Count > 0;
        if (!hasServices && !hasParts)
        {
            throw new DomainException("A OS deve conter ao menos um serviço ou uma peça/insumo.");
        }

        var normalizedDocument = DocumentValidator.Normalize(request.CustomerDocument);
        var customer = await _dbContext.Customers
            .FirstOrDefaultAsync(item => item.Document == normalizedDocument, cancellationToken);

        if (customer is null)
        {
            throw new DomainException("Cliente não encontrado para o CPF/CNPJ informado.");
        }

        var normalizedPlate = LicensePlateValidator.Normalize(request.Vehicle.LicensePlate);
        var vehicle = await _dbContext.Vehicles
            .FirstOrDefaultAsync(item => item.LicensePlate == normalizedPlate, cancellationToken);

        if (vehicle is null)
        {
            vehicle = new Vehicle(customer.Id, normalizedPlate, request.Vehicle.Brand, request.Vehicle.Model, request.Vehicle.Year);
            _dbContext.Vehicles.Add(vehicle);
        }
        else
        {
            if (vehicle.CustomerId != customer.Id)
            {
                throw new DomainException("A placa informada já está vinculada a outro cliente.");
            }

            vehicle.Update(normalizedPlate, request.Vehicle.Brand, request.Vehicle.Model, request.Vehicle.Year);
        }

        var serviceLines = await BuildServiceLinesAsync(requestedServices, cancellationToken);
        var partLines = await BuildPartLinesAsync(requestedParts, cancellationToken);

        var workOrder = new WorkOrder(
            customer.Id,
            vehicle.Id,
            serviceLines,
            partLines,
            request.Notes,
            DateTime.UtcNow);

        _dbContext.WorkOrders.Add(workOrder);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetRequiredByIdAsync(workOrder.Id, cancellationToken);
    }

    public async Task<IReadOnlyCollection<WorkOrder>> ListAsync(CancellationToken cancellationToken)
    {
        var workOrders = await BaseQuery()
            .AsNoTracking()
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return workOrders;
    }

    public async Task<WorkOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await BaseQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    public async Task<WorkOrder> TransitionAsync(Guid id, WorkOrderAction action, CancellationToken cancellationToken)
    {
        var workOrder = await _dbContext.WorkOrders
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (workOrder is null)
        {
            throw new KeyNotFoundException("Ordem de serviço não encontrada.");
        }

        var changedAt = DateTime.UtcNow;
        switch (action)
        {
            case WorkOrderAction.StartDiagnosis:
                workOrder.StartDiagnosis(changedAt);
                break;
            case WorkOrderAction.SendBudget:
                workOrder.SendBudget(changedAt);
                break;
            case WorkOrderAction.ApproveBudget:
                workOrder.ApproveBudget(changedAt);
                break;
            case WorkOrderAction.Finalize:
                workOrder.Finalize(changedAt);
                break;
            case WorkOrderAction.Deliver:
                workOrder.Deliver(changedAt);
                break;
            default:
                throw new DomainException("Ação de transição inválida.");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetRequiredByIdAsync(id, cancellationToken);
    }

    private IQueryable<WorkOrder> BaseQuery() =>
        _dbContext.WorkOrders
            .Include(item => item.Services)
            .Include(item => item.Parts)
            .Include(item => item.StatusHistory);

    private async Task<IReadOnlyCollection<WorkOrderServiceLine>> BuildServiceLinesAsync(
        IReadOnlyCollection<RequestedService> requestedServices,
        CancellationToken cancellationToken)
    {
        if (requestedServices.Count == 0)
        {
            return Array.Empty<WorkOrderServiceLine>();
        }

        var serviceIds = requestedServices.Select(item => item.ServiceId).Distinct().ToList();
        var services = await _dbContext.RepairServices
            .Where(item => serviceIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        if (services.Count != serviceIds.Count)
        {
            throw new DomainException("Um ou mais serviços informados não existem.");
        }

        return requestedServices
            .Select(item =>
            {
                if (item.Quantity <= 0)
                {
                    throw new DomainException("A quantidade de serviços deve ser maior que zero.");
                }

                var service = services[item.ServiceId];
                return new WorkOrderServiceLine(service.Id, service.Name, item.Quantity, service.LaborPrice);
            })
            .ToList();
    }

    private async Task<IReadOnlyCollection<WorkOrderPartLine>> BuildPartLinesAsync(
        IReadOnlyCollection<RequestedPart> requestedParts,
        CancellationToken cancellationToken)
    {
        if (requestedParts.Count == 0)
        {
            return Array.Empty<WorkOrderPartLine>();
        }

        var partIds = requestedParts.Select(item => item.PartId).Distinct().ToList();
        var parts = await _dbContext.PartSupplies
            .Where(item => partIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        if (parts.Count != partIds.Count)
        {
            throw new DomainException("Uma ou mais peças/insumos informados não existem.");
        }

        var partLines = new List<WorkOrderPartLine>();
        foreach (var requestedPart in requestedParts)
        {
            if (requestedPart.Quantity <= 0)
            {
                throw new DomainException("A quantidade de peças/insumos deve ser maior que zero.");
            }

            var part = parts[requestedPart.PartId];
            part.RemoveFromStock(requestedPart.Quantity);
            partLines.Add(new WorkOrderPartLine(part.Id, part.Name, requestedPart.Quantity, part.UnitPrice));
        }

        return partLines;
    }

    private async Task<WorkOrder> GetRequiredByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var workOrder = await GetByIdAsync(id, cancellationToken);
        return workOrder ?? throw new KeyNotFoundException("Ordem de serviço não encontrada.");
    }
}
