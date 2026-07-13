using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Application.Services;

namespace OficinaMvp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/customers")]
public sealed class CustomersController : ControllerBase
{
    private readonly WorkshopCatalogApplicationService _catalogService;

    public CustomersController(WorkshopCatalogApplicationService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<CustomerResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var customers = await _catalogService.ListCustomersAsync(cancellationToken);
        return Ok(customers.Select(item => item.ToResponse()).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var customer = await _catalogService.GetCustomerByIdAsync(id, cancellationToken);
        return customer is null ? NotFound() : Ok(customer.ToResponse());
    }

    [HttpPost]
    public async Task<ActionResult<CustomerResponse>> Create([FromBody] UpsertCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await _catalogService.CreateCustomerAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = customer.Id }, customer.ToResponse());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomerResponse>> Update(Guid id, [FromBody] UpsertCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await _catalogService.UpdateCustomerAsync(id, request, cancellationToken);
        return customer is null ? NotFound() : Ok(customer.ToResponse());
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _catalogService.DeleteCustomerAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
