using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OficinaMvp.Api.Application.Contracts;
using OficinaMvp.Api.Domain.Entities;
using OficinaMvp.Api.Infrastructure.Persistence;

namespace OficinaMvp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/vehicles")]
public sealed class VehiclesController : ControllerBase
{
    private readonly WorkshopDbContext _dbContext;

    public VehiclesController(WorkshopDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<VehicleResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var vehicles = await _dbContext.Vehicles
            .AsNoTracking()
            .OrderBy(item => item.Brand)
            .ThenBy(item => item.Model)
            .Select(item => item.ToResponse())
            .ToListAsync(cancellationToken);

        return Ok(vehicles);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VehicleResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var vehicle = await _dbContext.Vehicles
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (vehicle is null)
        {
            return NotFound();
        }

        return Ok(vehicle.ToResponse());
    }

    [HttpPost]
    public async Task<ActionResult<VehicleResponse>> Create([FromBody] UpsertVehicleRequest request, CancellationToken cancellationToken)
    {
        var customerExists = await _dbContext.Customers
            .AnyAsync(item => item.Id == request.CustomerId, cancellationToken);

        if (!customerExists)
        {
            return NotFound(new { message = "Cliente não encontrado para vincular veículo." });
        }

        var vehicle = new Vehicle(request.CustomerId, request.LicensePlate, request.Brand, request.Model, request.Year);
        _dbContext.Vehicles.Add(vehicle);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = vehicle.Id }, vehicle.ToResponse());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<VehicleResponse>> Update(Guid id, [FromBody] UpsertVehicleRequest request, CancellationToken cancellationToken)
    {
        var vehicle = await _dbContext.Vehicles.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (vehicle is null)
        {
            return NotFound();
        }

        if (vehicle.CustomerId != request.CustomerId)
        {
            return BadRequest(new { message = "O cliente do veículo não pode ser alterado nesse endpoint." });
        }

        vehicle.Update(request.LicensePlate, request.Brand, request.Model, request.Year);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(vehicle.ToResponse());
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var vehicle = await _dbContext.Vehicles.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (vehicle is null)
        {
            return NotFound();
        }

        _dbContext.Vehicles.Remove(vehicle);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
