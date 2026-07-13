using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Application.Ports;
using OficinaMvp.Application.Services;
using OficinaMvp.Domain.Validation;

namespace OficinaMvp.Api.Controllers;

[ApiController]
[AllowAnonymous]
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
    public async Task<ActionResult<ClientTrackingResponse>> Track(Guid id, [FromQuery] string document, CancellationToken cancellationToken)
    {
        var workOrder = await _workOrderService.GetByIdAsync(id, cancellationToken);
        if (workOrder is null)
        {
            return NotFound();
        }

        var normalizedDocument = DocumentValidator.Normalize(document);
        var customer = await _repository.GetCustomerByIdAsync(workOrder.CustomerId, trackChanges: false, cancellationToken);

        if (customer is null || customer.Document != normalizedDocument)
        {
            return NotFound();
        }

        return Ok(workOrder.ToClientTrackingResponse(customer.Document));
    }
}
