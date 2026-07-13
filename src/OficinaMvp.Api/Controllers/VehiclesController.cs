using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Application.Services;

namespace OficinaMvp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/vehicles")]
public sealed class VehiclesController : ControllerBase
{
    private readonly WorkshopCatalogApplicationService _catalogService;

    public VehiclesController(WorkshopCatalogApplicationService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<VehicleResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var vehicles = await _catalogService.ListVehiclesAsync(cancellationToken);
        return Ok(vehicles.Select(item => item.ToResponse()).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VehicleResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var vehicle = await _catalogService.GetVehicleByIdAsync(id, cancellationToken);
        return vehicle is null ? NotFound() : Ok(vehicle.ToResponse());
    }

    [HttpPost]
    public async Task<ActionResult<VehicleResponse>> Create([FromBody] UpsertVehicleRequest request, CancellationToken cancellationToken)
    {
        var vehicle = await _catalogService.CreateVehicleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = vehicle.Id }, vehicle.ToResponse());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<VehicleResponse>> Update(Guid id, [FromBody] UpsertVehicleRequest request, CancellationToken cancellationToken)
    {
        var vehicle = await _catalogService.UpdateVehicleAsync(id, request, cancellationToken);
        return vehicle is null ? NotFound() : Ok(vehicle.ToResponse());
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _catalogService.DeleteVehicleAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
