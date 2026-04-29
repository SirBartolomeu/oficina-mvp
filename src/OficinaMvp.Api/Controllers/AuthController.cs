using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OficinaMvp.Api.Application.Contracts;
using OficinaMvp.Api.Infrastructure.Security;

namespace OficinaMvp.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AdminCredentialsOptions _adminCredentials;
    private readonly JwtOptions _jwtOptions;
    private readonly ITokenService _tokenService;

    public AuthController(
        IOptions<AdminCredentialsOptions> adminCredentials,
        IOptions<JwtOptions> jwtOptions,
        ITokenService tokenService)
    {
        _adminCredentials = adminCredentials.Value;
        _jwtOptions = jwtOptions.Value;
        _tokenService = tokenService;
    }

    [AllowAnonymous]
    [HttpPost("token")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<TokenResponse> GenerateToken([FromBody] TokenRequest request)
    {
        var isValidUser = request.Username == _adminCredentials.Username &&
                          request.Password == _adminCredentials.Password;

        if (!isValidUser)
        {
            return Unauthorized(new { message = "Credenciais inválidas." });
        }

        var token = _tokenService.GenerateToken(request.Username);
        return Ok(new TokenResponse(token, "Bearer", _jwtOptions.ExpirationMinutes * 60));
    }
}
