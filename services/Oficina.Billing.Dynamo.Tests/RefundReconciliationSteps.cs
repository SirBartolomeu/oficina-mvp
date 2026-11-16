using Reqnroll;
using Xunit;

namespace Oficina.Billing.Dynamo.Tests;

[Binding]
public sealed class RefundReconciliationSteps
{
    private bool _assertionsPassed;

    [Given("que o DynamoDB Local está configurado para o Billing")]
    public void GivenDynamoDbLocal() =>
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BILLING_DYNAMO_ENDPOINT")));

    [When("o POST do estorno tem resposta (.*)")]
    public async Task WhenRefundPostReturns(string response)
    {
        var ambiguous = response switch
        {
            "ambígua" => true,
            "conclusiva" => false,
            _ => throw new ArgumentOutOfRangeException(nameof(response), response, null)
        };
        await new RefundOrderProcessorTests()
            .RefundIsConfirmedOnlyByGetAndDoesNotRepeatAmbiguousPost(ambiguous);
        _assertionsPassed = true;
    }

    [Then("o Billing não repete o POST e publica RefundConfirmed após confirmação por GET")]
    public void ThenRefundConfirmedOnce() => Assert.True(_assertionsPassed);
}
