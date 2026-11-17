using Reqnroll;
using Xunit;

namespace Oficina.Execution.Postgres.Tests;

[Binding]
public sealed class ExecutionOutboxConcurrencySteps
{
    private bool _assertionsPassed;

    [Given("que a Outbox da Execução usa PostgreSQL isolado")]
    public void GivenPostgres() =>
        Assert.False(string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("EXECUTION_OUTBOX_POSTGRES_CONNECTION_STRING")));

    [When("dois dispatchers da Execução disputam a mesma mensagem")]
    public async Task WhenDispatchersCompete()
    {
        await new ExecutionOutboxConcurrencyTests().TwoDispatchersSelectOneOutboxMessageOnlyOnce();
        _assertionsPassed = true;
    }

    [Then("o evento da Execução é publicado uma vez")]
    public void ThenPublishedOnce() => Assert.True(_assertionsPassed);
}
