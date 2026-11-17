using Oficina.Execution.Persistence;
using Xunit;

namespace Oficina.Execution.Tests;

public sealed class ExecutionOutboxRetryTests
{
    [Fact]
    public void FailureDefersRetryAndSuccessClearsIt()
    {
        var now = DateTime.UtcNow;
        var message = new ExecutionOutboxMessage(Guid.NewGuid(), "StockReserved", "{}", now);

        message.MarkFailed("fila indisponível", now);
        Assert.Equal(1, message.PublishAttempts);
        Assert.Equal(now.AddSeconds(2), message.NextAttemptAtUtc);
        Assert.Equal("fila indisponível", message.LastError);
        Assert.Null(message.PublishedAtUtc);

        message.MarkFailed("fila ainda indisponível", now.AddSeconds(2));
        Assert.Equal(2, message.PublishAttempts);
        Assert.Equal(now.AddSeconds(6), message.NextAttemptAtUtc);

        message.MarkPublished(now.AddSeconds(6));
        Assert.Equal(3, message.PublishAttempts);
        Assert.Null(message.NextAttemptAtUtc);
        Assert.Null(message.LastError);
        Assert.Equal(now.AddSeconds(6), message.PublishedAtUtc);
    }
}
