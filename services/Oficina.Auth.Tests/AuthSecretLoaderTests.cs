using System.Text.Json;
using Oficina.Auth;
using Xunit;

namespace Oficina.Auth.Tests;

public sealed class AuthSecretLoaderTests
{
    [Fact]
    public void ReadsTheSameSecretKeysCreatedForOsBootstrap()
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["Auth__LookupToken"] = new string('a', 64),
            ["Jwt__Key"] = new string('b', 64)
        });

        var options = AuthSecretLoader.Parse(json, new Uri("http://os.internal/lookup"),
            "OficinaMvp", "OficinaMvpClients");

        Assert.Equal(new string('a', 64), options.LookupToken);
        Assert.Equal(new string('b', 64), options.JwtKey);
    }

    [Fact]
    public void RejectsIncompleteOsSecret()
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["Auth__LookupToken"] = new string('a', 64),
            ["Jwt__Key"] = "short"
        });

        Assert.Throws<InvalidOperationException>(() => AuthSecretLoader.Parse(json,
            new Uri("http://os.internal/lookup"), "OficinaMvp", "OficinaMvpClients"));
    }
}
