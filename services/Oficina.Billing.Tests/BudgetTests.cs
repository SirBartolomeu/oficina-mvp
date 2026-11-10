using Oficina.Billing;
using Xunit;

namespace Oficina.Billing.Tests;

public sealed class BudgetTests
{
    [Fact]
    public void Create_ShouldKeepExactAmountInCents()
    {
        var budget = Budget.Create(Guid.NewGuid(), 1, "sp-centro",
            [new BudgetLine(Guid.NewGuid(), "Serviço", 2, 1250)]);
        Assert.Equal(2500, budget.TotalCents);
        Assert.Equal(BudgetStatus.AwaitingApproval, budget.Status);
        Assert.Equal("sp-centro", budget.BranchId);
    }

    [Fact]
    public void Decision_ShouldBeImmutableAndSingleUse()
    {
        var original = Budget.Create(Guid.NewGuid(), 1, null,
            [new BudgetLine(Guid.NewGuid(), "Peça", 1, 500)]);
        var approved = original.Decide(true);
        Assert.Equal(BudgetStatus.AwaitingApproval, original.Status);
        Assert.Equal(BudgetStatus.Approved, approved.Status);
        Assert.Throws<InvalidOperationException>(() => approved.Decide(false));
    }

    [Fact]
    public void Create_ShouldRejectInvalidLines()
    {
        Assert.Throws<ArgumentException>(() => Budget.Create(Guid.NewGuid(), 1, null,
            [new BudgetLine(Guid.NewGuid(), "Peça", 0, 500)]));
    }
}
