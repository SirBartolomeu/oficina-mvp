using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Oficina.Auth;

public sealed record AuthOptions(Uri LookupUrl, string LookupToken,
    string JwtKey, string Issuer, string Audience)
{
    public void Validate()
    {
        if (LookupUrl.Scheme is not ("http" or "https") ||
            LookupToken.Length < 32 || JwtKey.Length < 32 ||
            string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience))
            throw new InvalidOperationException("Configuração da autenticação inválida.");
    }
}

public sealed record CustomerToken(string AccessToken, string TokenType, int ExpiresIn);
internal sealed record CustomerLookup(Guid CustomerId, bool IsActive);

public sealed class CustomerTokenService(HttpClient http, AuthOptions options,
    TimeProvider? timeProvider = null)
{
    private const int TokenLifetimeSeconds = 15 * 60;

    public async Task<CustomerToken?> AuthenticateAsync(string? suppliedCpf,
        CancellationToken cancellationToken)
    {
        options.Validate();
        if (!CpfValidator.TryNormalize(suppliedCpf, out var cpf)) return null;
        using var request = new HttpRequestMessage(HttpMethod.Post, options.LookupUrl)
        {
            Content = JsonContent.Create(new { cpf })
        };
        request.Headers.Add("X-Internal-Token", options.LookupToken);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        CustomerLookup? customer;
        try
        {
            customer = await response.Content.ReadFromJsonAsync<CustomerLookup>(
                cancellationToken: cancellationToken);
        }
        catch (System.Text.Json.JsonException error)
        {
            throw new HttpRequestException("Consulta de cliente retornou JSON inválido.", error);
        }
        if (customer is null || customer.CustomerId == Guid.Empty)
            throw new HttpRequestException("Consulta de cliente retornou dados inválidos.");
        if (!customer.IsActive) return null;

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, customer.CustomerId.ToString("D")),
            new Claim(ClaimTypes.Role, "Customer"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.JwtKey)),
            SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(options.Issuer, options.Audience, claims,
            now.UtcDateTime, now.AddSeconds(TokenLifetimeSeconds).UtcDateTime,
            credentials);
        return new CustomerToken(new JwtSecurityTokenHandler().WriteToken(jwt),
            "Bearer", TokenLifetimeSeconds);
    }
}
