using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OficinaMvp.Api.Application.Contracts;
using OficinaMvp.Api.Application.Services;
using OficinaMvp.Api.Domain.Validation;
using OficinaMvp.Api.Infrastructure.Persistence;

namespace OficinaMvp.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/client/work-orders")]
public sealed class ClientTrackingController : ControllerBase
{
    private readonly WorkOrderApplicationService _workOrderService;
    private readonly WorkshopDbContext _dbContext;

    public ClientTrackingController(WorkOrderApplicationService workOrderService, WorkshopDbContext dbContext)
    {
        _workOrderService = workOrderService;
        _dbContext = dbContext;
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
        var customer = await _dbContext.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == workOrder.CustomerId, cancellationToken);

        if (customer is null || customer.Document != normalizedDocument)
        {
            return NotFound();
        }

        return Ok(workOrder.ToClientTrackingResponse(customer.Document));
    }
}
