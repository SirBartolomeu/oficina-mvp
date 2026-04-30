using OficinaMvp.Api.Domain.Entities;
using OficinaMvp.Api.Domain.Exceptions;

namespace OficinaMvp.Domain.Tests.WorkOrders;

public sealed class WorkOrderTests
{
    [Fact]
    public void ShouldCalculateBudget_WhenWorkOrderIsCreated()
    {
        var workOrder = BuildDefaultWorkOrder();

        Assert.Equal(120m, workOrder.ServicesTotal);
        Assert.Equal(80m, workOrder.PartsTotal);
        Assert.Equal(200m, workOrder.BudgetTotal);
        Assert.Equal(WorkOrderStatus.Received, workOrder.Status);
    }

    [Fact]
    public void ShouldTransitionThroughWorkflow_WhenActionsAreValid()
    {
        var now = DateTime.UtcNow;
        var workOrder = BuildDefaultWorkOrder(now);

        workOrder.StartDiagnosis(now.AddMinutes(5));
        workOrder.SendBudget(now.AddMinutes(15));
        workOrder.ApproveBudget(now.AddMinutes(30));
        workOrder.Finalize(now.AddHours(2));
        workOrder.Deliver(now.AddHours(3));

        Assert.Equal(WorkOrderStatus.Delivered, workOrder.Status);
        Assert.Equal(6, workOrder.StatusHistory.Count);
    }

    [Fact]
    public void ShouldThrow_WhenTransitionIsInvalid()
    {
        var workOrder = BuildDefaultWorkOrder();

        Assert.Throws<DomainException>(() => workOrder.Deliver(DateTime.UtcNow));
    }

    [Fact]
    public void ShouldThrow_WhenCreatedWithoutServicesAndParts()
    {
        Assert.Throws<DomainException>(() => new WorkOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Array.Empty<WorkOrderServiceLine>(),
            Array.Empty<WorkOrderPartLine>(),
            null,
            DateTime.UtcNow));
    }

    [Fact]
    public void SendBudget_ShouldBeAllowed_FromReceivedStatus()
    {
        var createdAt = DateTime.UtcNow;
        var workOrder = BuildDefaultWorkOrder(createdAt);

        workOrder.SendBudget(createdAt.AddMinutes(1));

        Assert.Equal(WorkOrderStatus.AwaitingApproval, workOrder.Status);
        Assert.NotNull(workOrder.BudgetSentAtUtc);
    }

    private static WorkOrder BuildDefaultWorkOrder(DateTime? now = null)
    {
        var createdAt = now ?? DateTime.UtcNow;
        var service = new WorkOrderServiceLine(Guid.NewGuid(), "Troca de óleo", 1, 120m);
        var part = new WorkOrderPartLine(Guid.NewGuid(), "Filtro de óleo", 2, 40m);

        return new WorkOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new[] { service },
            new[] { part },
            "OS para revisão básica.",
            createdAt);
    }
}
