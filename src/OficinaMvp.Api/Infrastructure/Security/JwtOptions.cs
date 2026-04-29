namespace OficinaMvp.Api.Infrastructure.Security;

public sealed class JwtOptions
{
    public string Issuer { get; init; } = "OficinaMvp";
    public string Audience { get; init; } = "OficinaMvpClients";
    public string Key { get; init; } = string.Empty;
    public int ExpirationMinutes { get; init; } = 120;
}
