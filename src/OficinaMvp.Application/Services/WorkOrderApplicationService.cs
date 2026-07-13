using OficinaMvp.Application.Contracts;
using OficinaMvp.Application.Ports;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Domain.Exceptions;
using OficinaMvp.Domain.Validation;

namespace OficinaMvp.Application.Services;

public sealed class WorkOrderApplicationService
{
    private readonly IWorkshopRepository _repository;
    private readonly IWorkOrderStatusNotifier _notifier;

    public WorkOrderApplicationService(IWorkshopRepository repository, IWorkOrderStatusNotifier notifier)
    {
        _repository = repository;
        _notifier = notifier;
    }

    public async Task<WorkOrder> CreateAsync(CreateWorkOrderRequest request, CancellationToken cancellationToken)
    {
        var requestedServices = request.Services ?? Array.Empty<RequestedService>();
        var requestedParts = request.Parts ?? Array.Empty<RequestedPart>();

        if (requestedServices.Count == 0 && requestedParts.Count == 0)
        {
            throw new DomainException("A OS deve conter ao menos um servico ou uma peca/insumo.");
        }

        var normalizedDocument = DocumentValidator.Normalize(request.CustomerDocument);
        var customer = (await _repository.ListCustomersAsync(cancellationToken))
            .FirstOrDefault(item => item.Document == normalizedDocument);

        if (customer is null)
        {
            throw new DomainException("Cliente nao encontrado para o CPF/CNPJ informado.");
        }

        var normalizedPlate = LicensePlateValidator.Normalize(request.Vehicle.LicensePlate);
        var vehicle = await _repository.GetVehicleByLicensePlateAsync(normalizedPlate, trackChanges: true, cancellationToken);

        if (vehicle is null)
        {
            vehicle = new Vehicle(customer.Id, normalizedPlate, request.Vehicle.Brand, request.Vehicle.Model, request.Vehicle.Year);
            _repository.AddVehicle(vehicle);
        }
        else
        {
            if (vehicle.CustomerId != customer.Id)
            {
                throw new DomainException("A placa informada ja esta vinculada a outro cliente.");
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

        _repository.AddWorkOrder(workOrder);
        await _repository.SaveChangesAsync(cancellationToken);
        await NotifyLatestStatusAsync(workOrder, cancellationToken);

        return await GetRequiredByIdAsync(workOrder.Id, cancellationToken);
    }

    public async Task<IReadOnlyCollection<WorkOrder>> ListAsync(CancellationToken cancellationToken)
    {
        var workOrders = await _repository.ListWorkOrdersAsync(cancellationToken);

        return workOrders
            .Where(item => item.Status is not WorkOrderStatus.Finalized and not WorkOrderStatus.Delivered)
            .OrderBy(item => GetActiveListPriority(item.Status))
            .ThenBy(item => item.CreatedAtUtc)
            .ToList();
    }

    public async Task<WorkOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await _repository.GetWorkOrderByIdAsync(id, trackChanges: false, cancellationToken);

    public async Task<WorkOrderStatusResponse?> GetStatusByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var workOrder = await GetByIdAsync(id, cancellationToken);
        return workOrder?.ToStatusResponse();
    }

    public async Task<WorkOrder> TransitionAsync(Guid id, WorkOrderAction action, CancellationToken cancellationToken)
    {
        var workOrder = await _repository.GetWorkOrderByIdAsync(id, trackChanges: true, cancellationToken);
        if (workOrder is null)
        {
            throw new KeyNotFoundException("Ordem de servico nao encontrada.");
        }

        var changedAt = DateTime.UtcNow;
        ApplyTransition(workOrder, action, changedAt);

        await _repository.SaveChangesAsync(cancellationToken);
        await NotifyLatestStatusAsync(workOrder, cancellationToken);
        return await GetRequiredByIdAsync(id, cancellationToken);
    }

    public async Task<WorkOrder> ApplyBudgetDecisionAsync(Guid id, BudgetDecisionRequest request, CancellationToken cancellationToken)
    {
        var workOrder = await _repository.GetWorkOrderByIdAsync(id, trackChanges: true, cancellationToken);
        if (workOrder is null)
        {
            throw new KeyNotFoundException("Ordem de servico nao encontrada.");
        }

        var occurredAt = request.OccurredAtUtc ?? DateTime.UtcNow;
        if (request.Approved)
        {
            workOrder.ApproveBudget(occurredAt);
        }
        else
        {
            workOrder.RegisterBudgetRefusal(occurredAt, request.Reason);
        }

        await _repository.SaveChangesAsync(cancellationToken);
        await NotifyLatestStatusAsync(workOrder, cancellationToken);
        return await GetRequiredByIdAsync(id, cancellationToken);
    }

    public async Task<AverageExecutionTimeResponse> GetAverageExecutionTimeAsync(CancellationToken cancellationToken)
    {
        var finishedOrders = await _repository.ListFinishedWorkOrdersAsync(cancellationToken);
        var measuredOrders = finishedOrders
            .Where(item => item.ExecutionStartedAtUtc.HasValue && item.FinalizedAtUtc.HasValue)
            .ToList();

        if (measuredOrders.Count == 0)
        {
            return new AverageExecutionTimeResponse(0, 0);
        }

        var average = measuredOrders
            .Average(item => (item.FinalizedAtUtc!.Value - item.ExecutionStartedAtUtc!.Value).TotalMinutes);

        return new AverageExecutionTimeResponse(Math.Round(average, 2), measuredOrders.Count);
    }

    private static void ApplyTransition(WorkOrder workOrder, WorkOrderAction action, DateTime changedAt)
    {
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
                throw new DomainException("Acao de transicao invalida.");
        }
    }

    private async Task<IReadOnlyCollection<WorkOrderServiceLine>> BuildServiceLinesAsync(
        IReadOnlyCollection<RequestedService> requestedServices,
        CancellationToken cancellationToken)
    {
        if (requestedServices.Count == 0)
        {
            return Array.Empty<WorkOrderServiceLine>();
        }

        var serviceIds = requestedServices.Select(item => item.ServiceId).Distinct().ToList();
        var services = await _repository.GetRepairServicesByIdsAsync(serviceIds, cancellationToken);

        if (services.Count != serviceIds.Count)
        {
            throw new DomainException("Um ou mais servicos informados nao existem.");
        }

        return requestedServices
            .Select(item =>
            {
                if (item.Quantity <= 0)
                {
                    throw new DomainException("A quantidade de servicos deve ser maior que zero.");
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
        var parts = await _repository.GetPartSuppliesByIdsAsync(partIds, cancellationToken);

        if (parts.Count != partIds.Count)
        {
            throw new DomainException("Uma ou mais pecas/insumos informados nao existem.");
        }

        var partLines = new List<WorkOrderPartLine>();
        foreach (var requestedPart in requestedParts)
        {
            if (requestedPart.Quantity <= 0)
            {
                throw new DomainException("A quantidade de pecas/insumos deve ser maior que zero.");
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
        return workOrder ?? throw new KeyNotFoundException("Ordem de servico nao encontrada.");
    }

    private async Task NotifyLatestStatusAsync(WorkOrder workOrder, CancellationToken cancellationToken)
    {
        var latestStatus = workOrder.StatusHistory
            .OrderByDescending(item => item.ChangedAtUtc)
            .FirstOrDefault();

        if (latestStatus is null)
        {
            return;
        }

        await _notifier.NotifyAsync(
            new WorkOrderStatusNotification(
                workOrder.Id,
                workOrder.CustomerId,
                latestStatus.Status,
                latestStatus.Note,
                latestStatus.ChangedAtUtc),
            cancellationToken);
    }

    private static int GetActiveListPriority(WorkOrderStatus status) => status switch
    {
        WorkOrderStatus.InExecution => 1,
        WorkOrderStatus.AwaitingApproval => 2,
        WorkOrderStatus.InDiagnosis => 3,
        WorkOrderStatus.Received => 4,
        _ => 99
    };
}
