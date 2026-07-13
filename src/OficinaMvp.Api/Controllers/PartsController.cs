using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Application.Services;

namespace OficinaMvp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/parts")]
public sealed class PartsController : ControllerBase
{
    private readonly WorkshopCatalogApplicationService _catalogService;

    public PartsController(WorkshopCatalogApplicationService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<PartSupplyResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var parts = await _catalogService.ListPartSuppliesAsync(cancellationToken);
        return Ok(parts.Select(item => item.ToResponse()).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PartSupplyResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var part = await _catalogService.GetPartSupplyByIdAsync(id, cancellationToken);
        return part is null ? NotFound() : Ok(part.ToResponse());
    }

    [HttpPost]
    public async Task<ActionResult<PartSupplyResponse>> Create([FromBody] UpsertPartSupplyRequest request, CancellationToken cancellationToken)
    {
        var part = await _catalogService.CreatePartSupplyAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = part.Id }, part.ToResponse());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PartSupplyResponse>> Update(Guid id, [FromBody] UpsertPartSupplyRequest request, CancellationToken cancellationToken)
    {
        var part = await _catalogService.UpdatePartSupplyAsync(id, request, cancellationToken);
        return part is null ? NotFound() : Ok(part.ToResponse());
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _catalogService.DeletePartSupplyAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
