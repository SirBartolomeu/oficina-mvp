using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Application.Services;

namespace OficinaMvp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/work-orders")]
public sealed class WorkOrdersController : ControllerBase
{
    private readonly WorkOrderApplicationService _workOrderService;

    public WorkOrdersController(WorkOrderApplicationService workOrderService)
    {
        _workOrderService = workOrderService;
    }

    [HttpPost]
    public async Task<ActionResult<WorkOrderDetailResponse>> Create([FromBody] CreateWorkOrderRequest request, CancellationToken cancellationToken)
    {
        var workOrder = await _workOrderService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = workOrder.Id }, workOrder.ToDetailResponse());
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<WorkOrderSummaryResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var workOrders = await _workOrderService.ListAsync(cancellationToken);
        return Ok(workOrders.Select(item => item.ToSummaryResponse()).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkOrderDetailResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var workOrder = await _workOrderService.GetByIdAsync(id, cancellationToken);
        return workOrder is null ? NotFound() : Ok(workOrder.ToDetailResponse());
    }

    [HttpGet("{id:guid}/status")]
    public async Task<ActionResult<WorkOrderStatusResponse>> GetStatus(Guid id, CancellationToken cancellationToken)
    {
        var status = await _workOrderService.GetStatusByIdAsync(id, cancellationToken);
        return status is null ? NotFound() : Ok(status);
    }

    [HttpPost("{id:guid}/start-diagnosis")]
    public Task<ActionResult<WorkOrderDetailResponse>> StartDiagnosis(Guid id, CancellationToken cancellationToken) =>
        ApplyTransition(id, WorkOrderAction.StartDiagnosis, cancellationToken);

    [HttpPost("{id:guid}/send-budget")]
    public Task<ActionResult<WorkOrderDetailResponse>> SendBudget(Guid id, CancellationToken cancellationToken) =>
        ApplyTransition(id, WorkOrderAction.SendBudget, cancellationToken);

    [HttpPost("{id:guid}/approve-budget")]
    public Task<ActionResult<WorkOrderDetailResponse>> ApproveBudget(Guid id, CancellationToken cancellationToken) =>
        ApplyTransition(id, WorkOrderAction.ApproveBudget, cancellationToken);

    [HttpPost("{id:guid}/finalize")]
    public Task<ActionResult<WorkOrderDetailResponse>> FinalizeWork(Guid id, CancellationToken cancellationToken) =>
        ApplyTransition(id, WorkOrderAction.Finalize, cancellationToken);

    [HttpPost("{id:guid}/deliver")]
    public Task<ActionResult<WorkOrderDetailResponse>> Deliver(Guid id, CancellationToken cancellationToken) =>
        ApplyTransition(id, WorkOrderAction.Deliver, cancellationToken);

    [HttpGet("metrics/average-execution-time")]
    public async Task<ActionResult<AverageExecutionTimeResponse>> GetAverageExecutionTime(CancellationToken cancellationToken)
    {
        var response = await _workOrderService.GetAverageExecutionTimeAsync(cancellationToken);
        return Ok(response);
    }

    private async Task<ActionResult<WorkOrderDetailResponse>> ApplyTransition(
        Guid id,
        WorkOrderAction action,
        CancellationToken cancellationToken)
    {
        var workOrder = await _workOrderService.TransitionAsync(id, action, cancellationToken);
        return Ok(workOrder.ToDetailResponse());
    }
}
