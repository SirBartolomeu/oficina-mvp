using OficinaMvp.Domain.Entities;
using Reqnroll;
using Xunit;

namespace Oficina.Saga.Bdd.Tests;

[Binding]
public sealed class SagaTransitionsSteps
{
    private WorkOrder _order = null!;
    private readonly DateTime _now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    [Given("uma OS com orçamento disponível")]
    public void GivenBudgetReady()
    {
        _order = NewOrder();
        _order.AcceptBudgetReady(Guid.NewGuid(), 1, _now);
    }

    [Given("uma OS com estoque reservado aguardando pagamento")]
    public void GivenReservedStock()
    {
        GivenBudgetReady();
        WhenBudgetApproved();
        WhenStockConfirmed();
    }

    [Given("uma OS paga em execução")]
    public void GivenPaidExecution()
    {
        GivenReservedStock();
        _order.ConfirmPayment(_now);
        _order.StartExecutionAfterPayment(_now);
    }

    [When("o orçamento é aprovado")]
    public void WhenBudgetApproved() => _order.ApproveBudgetForPayment(_now, Guid.NewGuid());

    [When("a reserva é confirmada")]
    public void WhenStockConfirmed() => _order.ConfirmStockReservation(_now, Guid.NewGuid());

    [When("o pagamento termina como (.*) sem valor recebido")]
    public void WhenPaymentFails(string state) => _order.BeginStockRelease(_now, state);

    [When("a liberação é confirmada")]
    public void WhenStockReleased() => _order.ConfirmStockRelease(_now);

    [When("a execução recusa o reparo")]
    public void WhenExecutionRejected() => _order.RegisterExecutionRejection(_now, "Peça indisponível");

    [When("a administração solicita o estorno")]
    public void WhenRefundRequested() => _order.RequestRefundAfterReview(Guid.NewGuid(), _now, "Reparo recusado");

    [When("o estorno é confirmado")]
    public void WhenRefundConfirmed() => _order.ConfirmRefund(_now);

    [Then("a OS aguarda a reserva de estoque")]
    public void ThenAwaitingReservation() => Assert.Equal(WorkOrderProcessState.AwaitingStockReservation, _order.ProcessState);

    [Then("a OS aguarda o pagamento")]
    public void ThenAwaitingPayment() => Assert.Equal(WorkOrderProcessState.AwaitingPayment, _order.ProcessState);

    [Then("a OS aguarda a liberação de estoque")]
    public void ThenReleasingStock() => Assert.Equal(WorkOrderProcessState.ReleasingStock, _order.ProcessState);

    [Then("a OS aguarda revisão manual")]
    public void ThenManualReview() => Assert.Equal(WorkOrderProcessState.ManualReview, _order.ProcessState);

    [Then("a OS aguarda a confirmação do estorno")]
    public void ThenRefundRequested() => Assert.Equal(WorkOrderProcessState.RefundRequested, _order.ProcessState);

    [Then("a OS fica cancelada")]
    public void ThenCanceled() => Assert.Equal(WorkOrderProcessState.Canceled, _order.ProcessState);

    private WorkOrder NewOrder() => new(Guid.NewGuid(), Guid.NewGuid(),
        [new WorkOrderServiceLine(Guid.NewGuid(), "Revisão", 1, 125m)],
        [], null, _now, "sp-centro");
}
