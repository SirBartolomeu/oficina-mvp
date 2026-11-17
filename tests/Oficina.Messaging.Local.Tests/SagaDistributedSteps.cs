using OficinaMvp.Domain.Entities;
using Reqnroll;
using Xunit;

namespace Oficina.Messaging.Local.Tests;

[Binding]
public sealed class SagaDistributedSteps
{
    private bool _assertionsPassed;

    [Given("que os emuladores SQS e DynamoDB estão configurados")]
    public void GivenSqsAndDynamo()
    {
        GivenSqs();
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BILLING_DYNAMO_ENDPOINT")));
    }

    [Given("que o emulador SQS está configurado")]
    public void GivenSqs() =>
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MESSAGING_SQS_ENDPOINT")));

    [When("a ordem percorre o fluxo de abertura até execução")]
    public async Task WhenOrderFlows() =>
        await RunAsync(() => new OpenedEventFlowTests().OpenedOrderMovesThroughApprovalPaymentAndExecution());

    [When("um pedido de estorno é processado pelos serviços")]
    public async Task WhenRefundFlows() =>
        await RunAsync(() => new RefundFlowTests().RefundRequestCrossesQueuesAndConfirmationCancelsOrder());

    [When("a Execução recusa um reparo já iniciado")]
    public async Task WhenExecutionRejectsRepair() =>
        await RunAsync(() => new ExecutionRejectionFlowTests()
            .RejectedRepairCrossesQueueAndMovesOrderToManualReviewOnce());

    [When("um pagamento sem valor recebido termina como (.*)")]
    public async Task WhenPaymentRejected(string state) =>
        await RunAsync(() => new StockReservationFlowTests()
            .ReservationResultUpdatesOrderWithoutOverselling(
                2, 3, WorkOrderProcessState.AwaitingPayment, false, state));

    [When("a confirmação de pagamento chega antes da reserva e é reentregue")]
    public async Task WhenPaymentArrivesEarly() =>
        await RunAsync(() => new OutOfOrderPaymentTests()
            .PaymentConfirmationRetriesAfterStockReservationArrives());

    [When("(.*) recebe uma mensagem inválida repetidamente")]
    public async Task WhenInvalidMessageReceived(string service) =>
        await RunAsync(() => new DeadLetterFlowTests().InvalidMessageMovesToDeadLetterQueue(service));

    [Then("o fluxo distribuído termina com as verificações de estado e idempotência aprovadas")]
    public void ThenAssertionsPassed() => Assert.True(_assertionsPassed);

    private async Task RunAsync(Func<Task> integrationFlow)
    {
        await integrationFlow();
        _assertionsPassed = true;
    }
}
