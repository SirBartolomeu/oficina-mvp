using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OficinaMvp.Api.Application.Contracts;
using OficinaMvp.Api.Domain.Entities;
using OficinaMvp.Api.Infrastructure.Persistence;

namespace OficinaMvp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/parts")]
public sealed class PartsController : ControllerBase
{
    private readonly WorkshopDbContext _dbContext;

    public PartsController(WorkshopDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<PartSupplyResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var parts = await _dbContext.PartSupplies
            .AsNoTracking()
            .OrderBy(item => item.Name)
            .Select(item => item.ToResponse())
            .ToListAsync(cancellationToken);

        return Ok(parts);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PartSupplyResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var part = await _dbContext.PartSupplies
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (part is null)
        {
            return NotFound();
        }

        return Ok(part.ToResponse());
    }

    [HttpPost]
    public async Task<ActionResult<PartSupplyResponse>> Create([FromBody] UpsertPartSupplyRequest request, CancellationToken cancellationToken)
    {
        var part = new PartSupply(request.Name, request.UnitPrice, request.StockQuantity);
        _dbContext.PartSupplies.Add(part);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = part.Id }, part.ToResponse());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PartSupplyResponse>> Update(Guid id, [FromBody] UpsertPartSupplyRequest request, CancellationToken cancellationToken)
    {
        var part = await _dbContext.PartSupplies.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (part is null)
        {
            return NotFound();
        }

        part.Update(request.Name, request.UnitPrice, request.StockQuantity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(part.ToResponse());
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var part = await _dbContext.PartSupplies.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (part is null)
        {
            return NotFound();
        }

        _dbContext.PartSupplies.Remove(part);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
