using OficinaMvp.Domain.Entities;
using OficinaMvp.Domain.Exceptions;

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
    public void ShouldRejectBranchIdentifierLongerThanContractLimit()
    {
        var service = new WorkOrderServiceLine(Guid.NewGuid(), "Troca de óleo", 1, 120m);

        Assert.Throws<DomainException>(() => new WorkOrder(
            Guid.NewGuid(), Guid.NewGuid(), new[] { service },
            Array.Empty<WorkOrderPartLine>(), null, DateTime.UtcNow,
            new string('x', 65)));
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

    [Fact]
    public void BudgetReady_ShouldMoveToApprovalOnceAndRejectConflictingBudget()
    {
        var order = BuildDefaultWorkOrder();
        var budgetId = Guid.NewGuid();
        var occurredAt = DateTime.UtcNow;

        order.AcceptBudgetReady(budgetId, 1, occurredAt);
        order.AcceptBudgetReady(budgetId, 1, occurredAt);

        Assert.Equal(WorkOrderStatus.AwaitingApproval, order.Status);
        Assert.Equal(WorkOrderProcessState.AwaitingApproval, order.ProcessState);
        Assert.Equal(budgetId, order.BudgetId);
        Assert.Equal(1, order.BudgetVersion);
        Assert.Equal(2, order.StatusHistory.Count);
        Assert.Throws<DomainException>(() => order.AcceptBudgetReady(Guid.NewGuid(), 1, occurredAt));
    }

    [Fact]
    public void DistributedFlow_ShouldWaitForReservationAndPaymentBeforeExecution()
    {
        var now = DateTime.UtcNow;
        var workOrder = BuildDefaultWorkOrder(now);
        workOrder.SendBudget(now.AddMinutes(1));
        workOrder.ApproveBudgetForPayment(now.AddMinutes(2));

        Assert.Equal(WorkOrderStatus.AwaitingApproval, workOrder.Status);
        Assert.Equal(WorkOrderProcessState.AwaitingStockReservation, workOrder.ProcessState);
        Assert.Null(workOrder.ExecutionStartedAtUtc);
        Assert.Throws<DomainException>(() => workOrder.StartExecutionAfterPayment(now.AddMinutes(3)));

        workOrder.ConfirmStockReservation(now.AddMinutes(4));
        Assert.Equal(WorkOrderProcessState.AwaitingPayment, workOrder.ProcessState);
        Assert.Throws<DomainException>(() => workOrder.StartExecutionAfterPayment(now.AddMinutes(5)));

        workOrder.ConfirmPayment(now.AddMinutes(6));
        workOrder.StartExecutionAfterPayment(now.AddMinutes(7));

        Assert.Equal(WorkOrderStatus.InExecution, workOrder.Status);
        Assert.Equal(WorkOrderProcessState.InExecution, workOrder.ProcessState);
        Assert.Equal(now.AddMinutes(7), workOrder.ExecutionStartedAtUtc);
    }

    [Fact]
    public void DistributedFlow_ShouldAllowCancellationBeforeExecutionAndRejectLateEvents()
    {
        var now = DateTime.UtcNow;
        var workOrder = BuildDefaultWorkOrder(now);
        workOrder.SendBudget(now.AddMinutes(1));
        workOrder.ApproveBudgetForPayment(now.AddMinutes(2));
        workOrder.ConfirmStockReservation(now.AddMinutes(3));
        workOrder.CancelBeforeExecution(now.AddMinutes(4), "Pagamento recusado");

        Assert.Equal(WorkOrderProcessState.Canceled, workOrder.ProcessState);
        Assert.Null(workOrder.ExecutionStartedAtUtc);
        Assert.Throws<DomainException>(() => workOrder.ConfirmPayment(now.AddMinutes(5)));
    }

    [Fact]
    public void StockReservationRejection_ShouldCancelBeforePayment()
    {
        var order = BuildDefaultWorkOrder();
        var reservationId = Guid.NewGuid();
        order.SendBudget(DateTime.UtcNow);
        order.ApproveBudgetForPayment(DateTime.UtcNow, reservationId);

        order.RejectStockReservation(DateTime.UtcNow, "Estoque insuficiente");

        Assert.Equal(reservationId, order.ReservationId);
        Assert.Equal(WorkOrderProcessState.Canceled, order.ProcessState);
        Assert.Null(order.ExecutionStartedAtUtc);
        Assert.Throws<DomainException>(() => order.ConfirmStockReservation(DateTime.UtcNow));
    }

    [Fact]
    public void RejectedPayment_ShouldWaitForStockReleaseBeforeCanceling()
    {
        var order = BuildDefaultWorkOrder();
        order.SendBudget(DateTime.UtcNow);
        order.ApproveBudgetForPayment(DateTime.UtcNow, Guid.NewGuid());
        order.ConfirmStockReservation(DateTime.UtcNow, Guid.NewGuid());

        order.BeginStockRelease(DateTime.UtcNow, "expired");
        Assert.Equal(WorkOrderProcessState.ReleasingStock, order.ProcessState);
        Assert.Throws<DomainException>(() => order.ConfirmPayment(DateTime.UtcNow));

        order.ConfirmStockRelease(DateTime.UtcNow);
        Assert.Equal(WorkOrderProcessState.Canceled, order.ProcessState);
    }

    private static WorkOrder BuildDefaultWorkOrder(DateTime? now = null)
    {
        var createdAt = now ?? DateTime.UtcNow;
        var service = new WorkOrderServiceLine(Guid.NewGuid(), "Troca de oleo", 1, 120m);
        var part = new WorkOrderPartLine(Guid.NewGuid(), "Filtro de oleo", 2, 40m);

        return new WorkOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new[] { service },
            new[] { part },
            "OS para revisao basica.",
            createdAt);
    }
}
