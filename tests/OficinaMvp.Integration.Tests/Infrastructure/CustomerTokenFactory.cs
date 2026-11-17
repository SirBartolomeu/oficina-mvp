using System.Net;
using System.Text;
using System.Text.Json;
using Oficina.Auth;

namespace OficinaMvp.Integration.Tests.Infrastructure;

internal static class CustomerTokenFactory
{
    public static async Task<string> CreateAsync(Guid customerId)
    {
        using var http = new HttpClient(new StubHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    customerId, isActive = true
                }), Encoding.UTF8, "application/json")
            })));
        var service = new CustomerTokenService(http, new AuthOptions(
            new Uri("http://os.local/internal/customers/lookup"),
            "local-lookup-token-with-at-least-32-characters",
            "OficinaMvp-ChaveJWT-SuperSecreta-Com-32-Caracteres",
            "OficinaMvp", "OficinaMvpClients"));
        return (await service.AuthenticateAsync("52998224725", CancellationToken.None))!
            .AccessToken;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => respond(request);
    }
}
