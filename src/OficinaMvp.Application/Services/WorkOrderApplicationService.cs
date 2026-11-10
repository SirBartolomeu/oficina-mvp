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
    private readonly IExecutionQuoteClient? _executionQuoteClient;
    private readonly IIntegrationOutbox _outbox;

    public WorkOrderApplicationService(
        IWorkshopRepository repository,
        IWorkOrderStatusNotifier notifier,
        IIntegrationOutbox outbox,
        IExecutionQuoteClient? executionQuoteClient = null)
    {
        _repository = repository;
        _notifier = notifier;
        _outbox = outbox;
        _executionQuoteClient = executionQuoteClient;
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

        IReadOnlyCollection<WorkOrderServiceLine> serviceLines;
        IReadOnlyCollection<WorkOrderPartLine> partLines;
        if (_executionQuoteClient is null)
        {
            serviceLines = await BuildServiceLinesAsync(requestedServices, cancellationToken);
            partLines = await BuildPartLinesAsync(requestedParts, cancellationToken);
        }
        else
        {
            var quote = await _executionQuoteClient.QuoteAsync(requestedServices, requestedParts, cancellationToken);
            serviceLines = BuildQuotedServiceLines(requestedServices, quote.Services);
            partLines = BuildQuotedPartLines(requestedParts, quote.Parts);
            var calculatedCents = serviceLines.Sum(item => checked((long)(item.Subtotal * 100m)))
                + partLines.Sum(item => checked((long)(item.Subtotal * 100m)));
            if (calculatedCents != quote.TotalCents)
                throw new DomainException("A cotação da Execução retornou total inconsistente.");
        }

        var workOrder = new WorkOrder(
            customer.Id,
            vehicle.Id,
            serviceLines,
            partLines,
            request.Notes,
            DateTime.UtcNow,
            request.BranchId);

        _repository.AddWorkOrder(workOrder);
        if (_executionQuoteClient is not null)
            _outbox.Enqueue(CreateOpenedEvent(workOrder));
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

    private static IReadOnlyCollection<WorkOrderServiceLine> BuildQuotedServiceLines(
        IReadOnlyCollection<RequestedService> requested,
        IReadOnlyCollection<ExecutionQuotedItem> quoted)
    {
        ValidateQuoteItems(requested.Select(item => (item.ServiceId, item.Quantity)), quoted);
        return quoted.Select(item => new WorkOrderServiceLine(item.Id, item.Name, item.Quantity,
            item.UnitPriceCents / 100m)).ToList();
    }

    private static IReadOnlyCollection<WorkOrderPartLine> BuildQuotedPartLines(
        IReadOnlyCollection<RequestedPart> requested,
        IReadOnlyCollection<ExecutionQuotedItem> quoted)
    {
        ValidateQuoteItems(requested.Select(item => (item.PartId, item.Quantity)), quoted);
        return quoted.Select(item => new WorkOrderPartLine(item.Id, item.Name, item.Quantity,
            item.UnitPriceCents / 100m)).ToList();
    }

    private static void ValidateQuoteItems(
        IEnumerable<(Guid Id, int Quantity)> requested,
        IReadOnlyCollection<ExecutionQuotedItem> quoted)
    {
        var expected = requested.GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
        if (quoted.Count != expected.Count || quoted.Any(item => item.Id == Guid.Empty ||
            string.IsNullOrWhiteSpace(item.Name) || item.Quantity <= 0 || item.UnitPriceCents < 0 ||
            !expected.TryGetValue(item.Id, out var quantity) || quantity != item.Quantity) ||
            quoted.Select(item => item.Id).Distinct().Count() != quoted.Count)
            throw new DomainException("A cotação da Execução não corresponde aos itens solicitados.");
    }

    private static IntegrationEnvelope<WorkOrderOpenedPayload> CreateOpenedEvent(WorkOrder order)
    {
        var lines = order.Services.Select(item => new WorkOrderOpenedLine(
                "Service", item.ServiceId, item.ServiceName, item.Quantity,
                checked((long)(item.UnitPrice * 100m))))
            .Concat(order.Parts.Select(item => new WorkOrderOpenedLine(
                "Part", item.PartSupplyId, item.PartName, item.Quantity,
                checked((long)(item.UnitPrice * 100m)))))
            .ToArray();
        var payload = new WorkOrderOpenedPayload(1, lines,
            checked((long)(order.BudgetTotal * 100m)), "BRL", order.CreatedAtUtc);
        return new IntegrationEnvelope<WorkOrderOpenedPayload>(
            Guid.NewGuid(), "WorkOrderOpened", 1, order.Id, Guid.NewGuid(),
            order.Id, order.BranchId, order.CreatedAtUtc, payload);
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
