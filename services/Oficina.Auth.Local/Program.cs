using System.Text.Json;
using Oficina.Auth;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 2048);
builder.Services.AddSingleton<Function>();

var app = builder.Build();
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapPost("/auth/token", async (HttpContext context, Function function) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var body = await reader.ReadToEndAsync(context.RequestAborted);
    var gatewayEvent = JsonSerializer.Serialize(new
    {
        requestContext = new { http = new { method = "POST" } },
        body,
        isBase64Encoded = false
    });
    using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(gatewayEvent));
    using var output = await function.FunctionHandler(input);
    using var response = await JsonDocument.ParseAsync(output, cancellationToken: context.RequestAborted);
    var root = response.RootElement;
    context.Response.StatusCode = root.GetProperty("statusCode").GetInt32();
    foreach (var header in root.GetProperty("headers").EnumerateObject())
        context.Response.Headers[header.Name] = header.Value.GetString();
    await context.Response.WriteAsync(root.GetProperty("body").GetString()!, context.RequestAborted);
});
app.Run();
