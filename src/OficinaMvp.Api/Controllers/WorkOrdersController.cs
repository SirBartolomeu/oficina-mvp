using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OficinaMvp.Api.Application.Contracts;
using OficinaMvp.Api.Application.Services;
using OficinaMvp.Api.Infrastructure.Persistence;

namespace OficinaMvp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/work-orders")]
public sealed class WorkOrdersController : ControllerBase
{
    private readonly WorkOrderApplicationService _workOrderService;
    private readonly WorkshopDbContext _dbContext;

    public WorkOrdersController(WorkOrderApplicationService workOrderService, WorkshopDbContext dbContext)
    {
        _workOrderService = workOrderService;
        _dbContext = dbContext;
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
        if (workOrder is null)
        {
            return NotFound();
        }

        return Ok(workOrder.ToDetailResponse());
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
        var finishedOrders = await _dbContext.WorkOrders
            .AsNoTracking()
            .Where(item => item.ExecutionStartedAtUtc.HasValue && item.FinalizedAtUtc.HasValue)
            .ToListAsync(cancellationToken);

        if (finishedOrders.Count == 0)
        {
            return Ok(new AverageExecutionTimeResponse(0, 0));
        }

        var average = finishedOrders
            .Average(item => (item.FinalizedAtUtc!.Value - item.ExecutionStartedAtUtc!.Value).TotalMinutes);

        return Ok(new AverageExecutionTimeResponse(Math.Round(average, 2), finishedOrders.Count));
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
