using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Application.Ports;
using OficinaMvp.Application.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace OficinaMvp.Api.Controllers;

[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/client/work-orders")]
public sealed class ClientTrackingController : ControllerBase
{
    private readonly WorkOrderApplicationService _workOrderService;
    private readonly IWorkshopRepository _repository;

    public ClientTrackingController(WorkOrderApplicationService workOrderService, IWorkshopRepository repository)
    {
        _workOrderService = workOrderService;
        _repository = repository;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClientTrackingResponse>> Track(Guid id, CancellationToken cancellationToken)
    {
        var subject = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
            User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(subject, out var customerId)) return Forbid();

        var workOrder = await _workOrderService.GetByIdAsync(id, cancellationToken);
        if (workOrder is null || workOrder.CustomerId != customerId)
        {
            return NotFound();
        }

        var customer = await _repository.GetCustomerByIdAsync(workOrder.CustomerId, trackChanges: false, cancellationToken);

        if (customer is null || !customer.IsActive)
        {
            return NotFound();
        }

        return Ok(workOrder.ToClientTrackingResponse());
    }
}
