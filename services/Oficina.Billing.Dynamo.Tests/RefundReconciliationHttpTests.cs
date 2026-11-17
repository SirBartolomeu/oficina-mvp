using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oficina.Billing;
using Xunit;

namespace Oficina.Billing.Dynamo.Tests;

[Collection("Billing HTTP environment")]
public sealed class RefundReconciliationHttpTests
{
    [Fact]
    public async Task BudgetEndpointsRequireTokenAndPersistApproval()
    {
        var endpoint = Environment.GetEnvironmentVariable("BILLING_DYNAMO_ENDPOINT")
            ?? throw new InvalidOperationException("Defina BILLING_DYNAMO_ENDPOINT para o teste local.");
        using var dynamo = new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = endpoint });
        var table = "billing_budget_http_" + Guid.NewGuid().ToString("N");
        await dynamo.CreateTableAsync(new CreateTableRequest
        {
            TableName = table, BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions = [new("pk", ScalarAttributeType.S), new("sk", ScalarAttributeType.S)],
            KeySchema = [new("pk", KeyType.HASH), new("sk", KeyType.RANGE)]
        });
        const string internalToken = "local-internal-token-for-budget-test-123456";
        using var environment = new TestEnvironment(new Dictionary<string, string>
        {
            ["InternalToken"] = internalToken, ["DynamoDb__Table"] = table,
            ["DynamoDb__ServiceUrl"] = endpoint
        });
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Testing"));
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);

        var workOrderId = Guid.NewGuid();
        var url = $"/internal/budgets/{workOrderId:D}/1";
        var request = new CreateBudgetRequest(workOrderId, 1, "sp-centro",
            [new BudgetLine(Guid.NewGuid(), "Revisão", 2, 1250)]);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/internal/budgets", request)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Internal-Token", internalToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/internal/budgets", request with { Lines = [] })).StatusCode);
        using var created = await client.PostAsJsonAsync("/internal/budgets", request);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var budget = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Equal(2500, budget.RootElement.GetProperty("totalCents").GetInt64());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);

        using var approved = await client.PostAsJsonAsync(url + "/decision",
            new BudgetDecisionRequest(true));
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        using var decision = JsonDocument.Parse(await approved.Content.ReadAsStringAsync());
        Assert.Equal(2, decision.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync(url + "/decision", new BudgetDecisionRequest(false))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/internal/payments/{Guid.NewGuid():D}")).StatusCode);
    }

    [Fact]
    public async Task InternalRouteRequiresTokenAndConfirmsRefundWithGetOnly()
    {
        var endpoint = Environment.GetEnvironmentVariable("BILLING_DYNAMO_ENDPOINT")
            ?? throw new InvalidOperationException("Defina BILLING_DYNAMO_ENDPOINT para o teste local.");
        using var dynamo = new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = endpoint });
        var table = "billing_refund_http_" + Guid.NewGuid().ToString("N");
        await dynamo.CreateTableAsync(new CreateTableRequest
        {
            TableName = table, BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions = [new("pk", ScalarAttributeType.S), new("sk", ScalarAttributeType.S)],
            KeySchema = [new("pk", KeyType.HASH), new("sk", KeyType.RANGE)]
        });
        var settings = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["DynamoDb:Table"] = table }).Build();
        var repository = new BillingRepository(dynamo, settings);
        var budget = Budget.Create(Guid.NewGuid(), 1, "sp-centro",
            [new BudgetLine(Guid.NewGuid(), "Revisão", 1, 12500)]);
        await repository.CreateAsync(budget, CancellationToken.None);
        await repository.DecideAsync(budget.WorkOrderId, 1, true, CancellationToken.None);
        foreach (var pending in await repository.GetPendingOutboxAsync(CancellationToken.None))
            await repository.MarkOutboxPublishedAsync(pending.EventId, CancellationToken.None);
        var payment = new PaymentRequest(Guid.NewGuid(), budget.WorkOrderId, budget.Id,
            1, budget.BranchId, budget.TotalCents, DateTime.UtcNow);
        await repository.RecordPaymentRequestAsync(Guid.NewGuid(), payment, CancellationToken.None);
        await repository.CompletePaymentCreationAsync(payment.Id,
            new MercadoPagoQrOrder("ORDTEST123", "qr-test", "created"), CancellationToken.None);
        await repository.ConfirmPaymentAsync(payment, "ORDTEST123", CancellationToken.None);
        foreach (var pending in await repository.GetPendingOutboxAsync(CancellationToken.None))
            await repository.MarkOutboxPublishedAsync(pending.EventId, CancellationToken.None);
        var refundId = Guid.NewGuid();
        await repository.RecordRefundRequestAsync(refundId, payment.Id,
            "Serviço não realizado", CancellationToken.None);
        await repository.BeginRefundSubmissionAsync(payment.Id, refundId, CancellationToken.None);

        var calls = 0;
        using var providerHttp = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            calls++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    id = "ORDTEST123", external_reference = $"pay_{payment.Id:N}",
                    status = "refunded", currency = "BRL", total_amount = "125.00",
                    transactions = new { payments = new[]
                    {
                        new { status = "refunded", paid_amount = "125.00" }
                    } }
                }), Encoding.UTF8, "application/json")
            };
        })) { BaseAddress = new Uri("https://api.mercadopago.com/") };
        var provider = new MercadoPagoQrClient(providerHttp,
            new MercadoPagoQrOptions("test-token", "test-pos"));
        const string internalToken = "local-internal-token-for-http-test-123456";
        using var environment = new TestEnvironment(new Dictionary<string, string>
        {
            ["InternalToken"] = internalToken, ["DynamoDb__Table"] = table,
            ["DynamoDb__ServiceUrl"] = endpoint,
            ["MercadoPago__AccessToken"] = "test-token",
            ["MercadoPago__ExternalPosId"] = "test-pos"
        });
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services => services.AddScoped(_ => provider));
        });
        using var client = factory.CreateClient();
        var url = $"/internal/payments/{payment.Id:D}/reconcile-refund";
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsync(url, null)).StatusCode);
        Assert.Equal(0, calls);
        client.DefaultRequestHeaders.Add("X-Internal-Token", internalToken);
        using var response = await client.PostAsync(url, null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Refunded", body.RootElement.GetProperty("status").GetString());
        Assert.True(body.RootElement.GetProperty("reconciled").GetBoolean());
        Assert.Equal(1, calls);
        using var duplicate = await client.PostAsync(url, null);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(1, calls);
        Assert.Equal("Refunded",
            (await repository.GetPaymentAttemptAsync(payment.Id, CancellationToken.None))!.Status);
        Assert.Empty(await repository.GetPendingRefundsAsync(CancellationToken.None));
        var result = Assert.Single(await repository.GetPendingOutboxAsync(CancellationToken.None));
        using var eventBody = JsonDocument.Parse(result.Body);
        Assert.Equal("RefundConfirmed", eventBody.RootElement.GetProperty("eventType").GetString());
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    private sealed class TestEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = new();

        public TestEnvironment(IReadOnlyDictionary<string, string> values)
        {
            foreach (var (key, value) in values)
            {
                _previous[key] = Environment.GetEnvironmentVariable(key);
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose()
        {
            foreach (var (key, value) in _previous)
                Environment.SetEnvironmentVariable(key, value);
        }
    }
}
