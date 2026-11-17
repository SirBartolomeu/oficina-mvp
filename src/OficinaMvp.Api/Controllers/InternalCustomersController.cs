using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OficinaMvp.Domain.Validation;
using OficinaMvp.Infrastructure.Persistence;

namespace OficinaMvp.Api.Controllers;

[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("internal/customers")]
public sealed class InternalCustomersController(WorkshopDbContext db, IConfiguration configuration)
    : ControllerBase
{
    [HttpPost("lookup")]
    public async Task<IActionResult> Lookup([FromBody] CustomerLookupRequest request,
        CancellationToken cancellationToken)
    {
        var expectedToken = configuration["Auth:LookupToken"];
        var suppliedToken = Request.Headers["X-Internal-Token"].ToString();
        if (string.IsNullOrWhiteSpace(expectedToken) || expectedToken.Length < 32 ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedToken), Encoding.UTF8.GetBytes(suppliedToken)))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Cpf) || !DocumentValidator.IsValid(request.Cpf))
            return BadRequest();
        var cpf = DocumentValidator.Normalize(request.Cpf);
        if (cpf.Length != 11) return BadRequest();

        var customer = await db.Customers.AsNoTracking()
            .Where(item => item.Document == cpf)
            .Select(item => new CustomerLookupResponse(item.Id, item.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
        return customer is null ? NotFound() : Ok(customer);
    }
}

public sealed record CustomerLookupRequest(string Cpf);
public sealed record CustomerLookupResponse(Guid CustomerId, bool IsActive);
