using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using OficinaMvp.Api.Middleware;
using OficinaMvp.Api.Messaging;
using OficinaMvp.Application.Ports;
using OficinaMvp.Application.Observability;
using OficinaMvp.Application.Services;
using OficinaMvp.Infrastructure.Execution;
using OficinaMvp.Infrastructure.Messaging;
using OficinaMvp.Infrastructure.Notifications;
using OficinaMvp.Infrastructure.Persistence;
using OficinaMvp.Infrastructure.Persistence.Repositories;
using OficinaMvp.Infrastructure.Security;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});

var otlpEndpoint = builder.Configuration["Observability:OtlpEndpoint"];
if (!string.IsNullOrWhiteSpace(otlpEndpoint))
{
    if (!Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var endpoint) ||
        endpoint.Scheme is not ("http" or "https"))
        throw new InvalidOperationException("Observability:OtlpEndpoint deve ser uma URL HTTP válida.");
    var sampleRatio = builder.Configuration.GetValue<double?>("Observability:TraceSampleRatio") ?? 0.1;
    if (sampleRatio is < 0 or > 1)
        throw new InvalidOperationException("Observability:TraceSampleRatio deve estar entre 0 e 1.");
    var metricIntervalSeconds = builder.Configuration.GetValue<int?>(
        "Observability:MetricExportIntervalSeconds") ?? 60;
    if (metricIntervalSeconds is < 5 or > 300)
        throw new InvalidOperationException(
            "Observability:MetricExportIntervalSeconds deve estar entre 5 e 300.");

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("oficina-os"))
        .WithTracing(tracing => tracing
            .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(sampleRatio)))
            .AddSource(SqsTraceContext.SourceName)
            .AddAspNetCoreInstrumentation(options =>
                options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(options => options.Endpoint = endpoint))
        .WithMetrics(metrics => metrics
            .AddMeter(WorkOrderMetrics.MeterName)
            .AddMeter("System.Runtime")
            .AddAspNetCoreInstrumentation()
            .AddOtlpExporter((options, readerOptions) =>
            {
                options.Endpoint = endpoint;
                readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds =
                    metricIntervalSeconds * 1000;
            }));
}

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<AdminCredentialsOptions>(builder.Configuration.GetSection("AdminCredentials"));
builder.Services.Configure<IntegrationOptions>(builder.Configuration.GetSection("Integration"));
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection("Notifications"));

var databaseProvider = builder.Configuration.GetValue<string>("Database:Provider") ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' nao configurada.");

builder.Services.AddDbContext<WorkshopDbContext>(options =>
{
    if (string.Equals(databaseProvider, "Postgres", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(databaseProvider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
    {
        options.UseNpgsql(connectionString);
        return;
    }

    options.UseSqlite(connectionString);
});

builder.Services.AddScoped<IWorkshopRepository, EfWorkshopRepository>();
builder.Services.AddScoped<IIntegrationOutbox, EfIntegrationOutbox>();
builder.Services.AddScoped<IWorkOrderStatusNotifier, WorkOrderStatusNotifier>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<WorkshopCatalogApplicationService>();
builder.Services.AddScoped<WorkOrderApplicationService>();

var billingQueueUrl = builder.Configuration["Messaging:BillingQueueUrl"];
var executionCommandsQueueUrl = builder.Configuration["Messaging:ExecutionCommandsQueueUrl"];
var osEventsQueueUrl = builder.Configuration["Messaging:OsEventsQueueUrl"];
if (!string.IsNullOrWhiteSpace(billingQueueUrl) ||
    !string.IsNullOrWhiteSpace(executionCommandsQueueUrl) ||
    !string.IsNullOrWhiteSpace(osEventsQueueUrl))
{
    var sqsServiceUrl = builder.Configuration["Messaging:SqsServiceUrl"];
    var region = RegionEndpoint.GetBySystemName(builder.Configuration["AWS:Region"] ?? "us-east-1");
    builder.Services.AddSingleton<IAmazonSQS>(_ => string.IsNullOrWhiteSpace(sqsServiceUrl)
        ? new AmazonSQSClient(region)
        : new AmazonSQSClient(new BasicAWSCredentials("local", "local"), new AmazonSQSConfig
        {
            ServiceURL = sqsServiceUrl
        }));
    if (!string.IsNullOrWhiteSpace(billingQueueUrl) || !string.IsNullOrWhiteSpace(executionCommandsQueueUrl))
    {
        var routes = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(billingQueueUrl)) routes["WorkOrderOpened"] = billingQueueUrl;
        if (!string.IsNullOrWhiteSpace(billingQueueUrl)) routes["CreatePaymentRequested"] = billingQueueUrl;
        if (!string.IsNullOrWhiteSpace(billingQueueUrl)) routes["RefundPaymentRequested"] = billingQueueUrl;
        if (!string.IsNullOrWhiteSpace(executionCommandsQueueUrl))
            routes["ReserveStockRequested"] = executionCommandsQueueUrl;
        if (!string.IsNullOrWhiteSpace(executionCommandsQueueUrl))
            routes["ReleaseStockRequested"] = executionCommandsQueueUrl;
        if (!string.IsNullOrWhiteSpace(executionCommandsQueueUrl))
            routes["ConsumeStockRequested"] = executionCommandsQueueUrl;
        if (!string.IsNullOrWhiteSpace(executionCommandsQueueUrl))
            routes["ExecutionRequested"] = executionCommandsQueueUrl;
        builder.Services.AddSingleton<IIntegrationMessageSender>(serviceProvider =>
            new SqsIntegrationMessageSender(serviceProvider.GetRequiredService<IAmazonSQS>(), routes));
        builder.Services.AddScoped<OutboxDispatcher>();
        builder.Services.AddHostedService<OutboxBackgroundService>();
    }
    if (!string.IsNullOrWhiteSpace(osEventsQueueUrl))
    {
        builder.Services.AddScoped<BudgetReadyHandler>();
        builder.Services.AddScoped<BudgetDecisionHandler>();
        builder.Services.AddScoped<StockReservationResultHandler>();
        builder.Services.AddScoped<PaymentConfirmedHandler>();
        builder.Services.AddScoped<PaymentRejectedHandler>();
        builder.Services.AddScoped<StockReleasedHandler>();
        builder.Services.AddScoped<StockConsumedHandler>();
        builder.Services.AddScoped<ExecutionStartedHandler>();
        builder.Services.AddScoped<ExecutionFinishedHandler>();
        builder.Services.AddScoped<ExecutionRejectedHandler>();
        builder.Services.AddScoped<RefundConfirmedHandler>();
        builder.Services.AddHostedService<OsEventsSqsWorker>();
    }
}

var executionBaseUrl = builder.Configuration["Execution:BaseUrl"];
var executionInternalToken = builder.Configuration["Execution:InternalToken"];
if (!string.IsNullOrWhiteSpace(executionBaseUrl))
{
    if (!Uri.TryCreate(executionBaseUrl, UriKind.Absolute, out var executionUri) ||
        string.IsNullOrWhiteSpace(executionInternalToken))
        throw new InvalidOperationException("Execution:BaseUrl e Execution:InternalToken devem ser válidos.");

    builder.Services.AddHttpClient("execution-quote", client =>
    {
        client.BaseAddress = new Uri(executionUri.ToString().TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(5);
    }).AddTypedClient<IExecutionQuoteClient>((client, _) =>
        new HttpExecutionQuoteClient(client, executionInternalToken));
}

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwtOptions.Key) || jwtOptions.Key.Length < 32)
{
    throw new InvalidOperationException("A chave JWT deve ter ao menos 32 caracteres.");
}

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin")));

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Oficina Tech Challenge API",
        Version = "v2",
        Description = "API para gestao de oficina mecanica com Clean Architecture, JWT, banco relacional e suporte a Kubernetes."
    });

    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Informe o token JWT no formato: Bearer {seu_token}",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = JwtBearerDefaults.AuthenticationScheme
        }
    };

    options.AddSecurityDefinition(securityScheme.Reference.Id, securityScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [securityScheme] = Array.Empty<string>()
    });
});

var app = builder.Build();

app.Use(async (context, next) =>
{
    var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    using var scope = app.Logger.BeginScope(new Dictionary<string, object>
    {
        ["traceId"] = traceId,
        ["requestId"] = context.TraceIdentifier
    });
    context.Response.Headers["X-Trace-Id"] = traceId;
    var startedAt = Stopwatch.GetTimestamp();
    await next();
    if (!context.Request.Path.StartsWithSegments("/health"))
        app.Logger.LogInformation("HTTP {Method} {StatusCode} {Endpoint} {ElapsedMs}",
            context.Request.Method, context.Response.StatusCode,
            context.GetEndpoint()?.DisplayName ?? "unmatched",
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
});

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<WorkshopDbContext>();
    var useMigrations = app.Configuration.GetValue<bool>("Database:UseMigrations");

    if (app.Environment.IsProduction() || useMigrations)
    {
        await dbContext.Database.MigrateAsync();
    }
    else
    {
        await dbContext.Database.EnsureCreatedAsync();
    }

    var shouldSeedDemoData = app.Configuration.GetValue<bool>("SeedDemoData") || app.Environment.IsDevelopment();
    if (shouldSeedDemoData && !app.Environment.IsEnvironment("Testing"))
    {
        await WorkshopDemoSeeder.SeedAsync(dbContext, app.Logger);
    }
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/health/ready", async (WorkshopDbContext dbContext, CancellationToken cancellationToken) =>
{
    var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
    return canConnect
        ? Results.Ok(new { status = "Ready" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
});

app.MapControllers();

app.Run();

public partial class Program;
