using Reqnroll;
using Xunit;

namespace OficinaMvp.Postgres.Tests;

[Binding]
public sealed class OutboxConcurrencySteps
{
    private bool _assertionsPassed;

    [Given("que a Outbox da OS usa PostgreSQL isolado")]
    public void GivenPostgres() =>
        Assert.False(string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("OS_POSTGRES_CONNECTION_STRING")));

    [When("dois dispatchers disputam a mesma mensagem")]
    public async Task WhenDispatchersCompete()
    {
        await new OutboxConcurrencyTests().TwoDispatchersDoNotPublishTheSamePendingMessage();
        _assertionsPassed = true;
    }

    [Then("a mensagem é publicada uma vez e marcada como publicada")]
    public void ThenPublishedOnce() => Assert.True(_assertionsPassed);
}
