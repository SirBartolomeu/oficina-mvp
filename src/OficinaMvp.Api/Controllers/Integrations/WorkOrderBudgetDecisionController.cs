using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Application.Services;
using OficinaMvp.Infrastructure.Security;

namespace OficinaMvp.Api.Controllers.Integrations;

[ApiController]
[AllowAnonymous]
[Route("api/integrations/work-orders")]
public sealed class WorkOrderBudgetDecisionController : ControllerBase
{
    private const string IntegrationTokenHeader = "X-Integration-Token";
    private readonly WorkOrderApplicationService _workOrderService;
    private readonly IntegrationOptions _options;

    public WorkOrderBudgetDecisionController(
        WorkOrderApplicationService workOrderService,
        IOptions<IntegrationOptions> options)
    {
        _workOrderService = workOrderService;
        _options = options.Value;
    }

    [HttpPost("{id:guid}/budget-decision")]
    public async Task<ActionResult<WorkOrderDetailResponse>> DecideBudget(
        Guid id,
        [FromBody] BudgetDecisionRequest request,
        CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(IntegrationTokenHeader, out var token) ||
            string.IsNullOrWhiteSpace(_options.Token) ||
            !string.Equals(token.ToString(), _options.Token, StringComparison.Ordinal))
        {
            return Unauthorized(new { message = "Token de integracao invalido." });
        }

        var workOrder = await _workOrderService.ApplyBudgetDecisionAsync(id, request, cancellationToken);
        return Ok(workOrder.ToDetailResponse());
    }
}
