using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Application.Services;

namespace OficinaMvp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/services")]
public sealed class ServicesController : ControllerBase
{
    private readonly WorkshopCatalogApplicationService _catalogService;

    public ServicesController(WorkshopCatalogApplicationService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<RepairServiceResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var services = await _catalogService.ListRepairServicesAsync(cancellationToken);
        return Ok(services.Select(item => item.ToResponse()).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RepairServiceResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var service = await _catalogService.GetRepairServiceByIdAsync(id, cancellationToken);
        return service is null ? NotFound() : Ok(service.ToResponse());
    }

    [HttpPost]
    public async Task<ActionResult<RepairServiceResponse>> Create([FromBody] UpsertRepairServiceRequest request, CancellationToken cancellationToken)
    {
        var service = await _catalogService.CreateRepairServiceAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = service.Id }, service.ToResponse());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RepairServiceResponse>> Update(Guid id, [FromBody] UpsertRepairServiceRequest request, CancellationToken cancellationToken)
    {
        var service = await _catalogService.UpdateRepairServiceAsync(id, request, cancellationToken);
        return service is null ? NotFound() : Ok(service.ToResponse());
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _catalogService.DeleteRepairServiceAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
