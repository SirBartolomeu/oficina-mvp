using System.Text.Json;

namespace Oficina.Auth;

public static class AuthSecretLoader
{
    public static AuthOptions Parse(string secretJson, Uri lookupUrl, string issuer, string audience)
    {
        using var secret = JsonDocument.Parse(secretJson);
        var root = secret.RootElement;
        var lookupToken = root.GetProperty("Auth__LookupToken").GetString();
        var jwtKey = root.GetProperty("Jwt__Key").GetString();
        var options = new AuthOptions(lookupUrl, lookupToken ?? "", jwtKey ?? "", issuer, audience);
        options.Validate();
        return options;
    }
}
