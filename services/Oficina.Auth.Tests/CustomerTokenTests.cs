using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Oficina.Auth;
using Xunit;

namespace Oficina.Auth.Tests;

public sealed class CustomerTokenTests
{
    private const string Key = "local-customer-jwt-key-with-at-least-32-characters";
    private const string LookupToken = "local-lookup-token-with-at-least-32-characters";
    private static readonly Guid CustomerId = Guid.Parse("d0a1c337-f628-4b8d-b998-2c52e123b3b0");

    [Theory]
    [InlineData("529.982.247-25", true)]
    [InlineData("52998224725", true)]
    [InlineData("000.000.000-00", false)]
    [InlineData("52998224726", false)]
    [InlineData("04.252.011/0001-10", false)]
    public void CpfUsesCheckDigits(string value, bool expected)
    {
        Assert.Equal(expected, CpfValidator.TryNormalize(value, out _));
    }

    [Fact]
    public async Task ActiveCustomerReceivesShortJwtWithoutCpfClaim()
    {
        var calls = 0;
        using var http = new HttpClient(new StubHandler(async request =>
        {
            calls++;
            Assert.Equal("http://os.local/internal/customers/lookup",
                request.RequestUri!.ToString());
            Assert.Equal(LookupToken, request.Headers.GetValues("X-Internal-Token").Single());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("52998224725", body.RootElement.GetProperty("cpf").GetString());
            return Json(HttpStatusCode.OK, new { customerId = CustomerId, isActive = true });
        }));
        var service = new CustomerTokenService(http, Options());
        var result = await service.AuthenticateAsync("529.982.247-25", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, calls);
        Assert.Equal(900, result.ExpiresIn);
        var principal = new JwtSecurityTokenHandler().ValidateToken(result.AccessToken,
            new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = "OficinaMvp",
                ValidateAudience = true, ValidAudience = "OficinaMvpClients",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
                ValidateLifetime = true, ClockSkew = TimeSpan.Zero
            }, out var validated);
        Assert.Equal(CustomerId.ToString("D"),
            principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.True(principal.IsInRole("Customer"));
        Assert.DoesNotContain(principal.Claims, claim => claim.Value.Contains("52998224725"));
        var jwt = Assert.IsType<JwtSecurityToken>(validated);
        Assert.InRange((jwt.ValidTo - jwt.ValidFrom).TotalSeconds, 899, 901);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.OK, false)]
    public async Task MissingOrInactiveCustomerGetsNoToken(HttpStatusCode status, bool active)
    {
        using var http = new HttpClient(new StubHandler(_ => Task.FromResult(
            Json(status, new { customerId = CustomerId, isActive = active }))));
        Assert.Null(await new CustomerTokenService(http, Options())
            .AuthenticateAsync("52998224725", CancellationToken.None));
    }

    [Fact]
    public async Task GatewayHandlerUsesGenericAuthFailureAndNoStoreResponse()
    {
        var calls = 0;
        using var http = new HttpClient(new StubHandler(_ =>
        {
            calls++;
            return Task.FromResult(Json(HttpStatusCode.NotFound, new { }));
        }));
        var function = new Function(new CustomerTokenService(http, Options()));
        using var invalid = await InvokeAsync(function, "POST", "00000000000");
        Assert.Equal(401, invalid.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(0, calls);
        using var missing = await InvokeAsync(function, "POST", "52998224725");
        Assert.Equal(401, missing.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(1, calls);
        Assert.Equal("no-store", missing.RootElement.GetProperty("headers")
            .GetProperty("cache-control").GetString());
        using var wrongMethod = await InvokeAsync(function, "GET", "52998224725");
        Assert.Equal(405, wrongMethod.RootElement.GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public async Task GatewayHandlerReturnsTokenForActiveCustomer()
    {
        using var http = new HttpClient(new StubHandler(_ => Task.FromResult(
            Json(HttpStatusCode.OK, new { customerId = CustomerId, isActive = true }))));
        var function = new Function(new CustomerTokenService(http, Options()));
        using var response = await InvokeAsync(function, "POST", "52998224725");
        Assert.Equal(200, response.RootElement.GetProperty("statusCode").GetInt32());
        using var body = JsonDocument.Parse(response.RootElement.GetProperty("body").GetString()!);
        Assert.Equal("Bearer", body.RootElement.GetProperty("tokenType").GetString());
        Assert.Equal(900, body.RootElement.GetProperty("expiresIn").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement
            .GetProperty("accessToken").GetString()));
    }

    [Fact]
    public async Task LookupFailureDoesNotIssueToken()
    {
        using var http = new HttpClient(new StubHandler(_ => Task.FromResult(
            Json(HttpStatusCode.ServiceUnavailable, new { }))));
        var function = new Function(new CustomerTokenService(http, Options()));
        using var response = await InvokeAsync(function, "POST", "52998224725");
        Assert.Equal(503, response.RootElement.GetProperty("statusCode").GetInt32());
        Assert.DoesNotContain("accessToken", response.RootElement.GetProperty("body").GetString());
    }

    private static async Task<JsonDocument> InvokeAsync(Function function, string method, string cpf)
    {
        var gatewayEvent = JsonSerializer.Serialize(new
        {
            requestContext = new { http = new { method } },
            body = JsonSerializer.Serialize(new { cpf }),
            isBase64Encoded = false
        });
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(gatewayEvent));
        using var output = await function.FunctionHandler(input);
        return await JsonDocument.ParseAsync(output);
    }

    private static AuthOptions Options() => new(
        new Uri("http://os.local/internal/customers/lookup"), LookupToken,
        Key, "OficinaMvp", "OficinaMvpClients");

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8,
            "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => respond(request);
    }
}
