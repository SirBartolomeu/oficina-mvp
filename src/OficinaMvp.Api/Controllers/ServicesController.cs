using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OficinaMvp.Api.Application.Contracts;
using OficinaMvp.Api.Domain.Entities;
using OficinaMvp.Api.Infrastructure.Persistence;

namespace OficinaMvp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/services")]
public sealed class ServicesController : ControllerBase
{
    private readonly WorkshopDbContext _dbContext;

    public ServicesController(WorkshopDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<RepairServiceResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var services = await _dbContext.RepairServices
            .AsNoTracking()
            .OrderBy(item => item.Name)
            .Select(item => item.ToResponse())
            .ToListAsync(cancellationToken);

        return Ok(services);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RepairServiceResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var service = await _dbContext.RepairServices
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (service is null)
        {
            return NotFound();
        }

        return Ok(service.ToResponse());
    }

    [HttpPost]
    public async Task<ActionResult<RepairServiceResponse>> Create([FromBody] UpsertRepairServiceRequest request, CancellationToken cancellationToken)
    {
        var service = new RepairService(request.Name, request.Description, request.LaborPrice, request.AverageDurationMinutes);
        _dbContext.RepairServices.Add(service);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = service.Id }, service.ToResponse());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RepairServiceResponse>> Update(Guid id, [FromBody] UpsertRepairServiceRequest request, CancellationToken cancellationToken)
    {
        var service = await _dbContext.RepairServices.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (service is null)
        {
            return NotFound();
        }

        service.Update(request.Name, request.Description, request.LaborPrice, request.AverageDurationMinutes);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(service.ToResponse());
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var service = await _dbContext.RepairServices.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (service is null)
        {
            return NotFound();
        }

        _dbContext.RepairServices.Remove(service);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
