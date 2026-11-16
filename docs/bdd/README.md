# Evidência BDD local

Os cenários em [saga-fase4.feature](saga-fase4.feature) descrevem comportamentos observáveis da integração entre serviços. Esse arquivo ainda é uma especificação rastreável, não é executado diretamente.

O projeto [Oficina.Saga.Bdd.Tests](../../tests/Oficina.Saga.Bdd.Tests) executa, com Reqnroll/xUnit, quatro casos Gherkin das transições centrais da OS: aprovação antes da reserva, liberação após `expired`/`canceled` e revisão/estorno depois da recusa. São testes reais do domínio, sem emuladores. As integrações entre serviços, outbox/inbox, duplicatas e DLQ continuam validadas pelos testes de integração abaixo; não afirmar que os quatro casos de domínio cobrem toda a Saga distribuída.

O projeto [Oficina.Messaging.Local.Tests](../../tests/Oficina.Messaging.Local.Tests) também executa oito casos em `SagaDistributed.feature`: abertura até execução, estorno Billing/OS, compensação por pagamento `expired`/`canceled`, confirmação antes da reserva com reentrega e DLQ de OS/Billing/Execução. Os passos acionam os testes de integração existentes como fluxos de caixa preta; as verificações detalhadas de estado, mensagens e idempotência permanecem nesses testes. A suíte inteira passou 21/21 em 16/11 com ElasticMQ e DynamoDB Local, incluindo os oito casos Gherkin. Isso ainda não cobre todos os cenários de `saga-fase4.feature` nem a AWS real.

```powershell
.\.dotnet_home\sdk10\dotnet.exe test tests/Oficina.Saga.Bdd.Tests --configuration Release
$env:MESSAGING_SQS_ENDPOINT='http://localhost:9324'
$env:BILLING_DYNAMO_ENDPOINT='http://localhost:8000'
.\.dotnet_home\sdk10\dotnet.exe test tests/Oficina.Messaging.Local.Tests --configuration Release
```

| Cenário | Testes executáveis existentes | Situação |
|---|---|---|
| Abertura → aprovação → reserva → pagamento → execução | `OpenedEventFlowTests.OpenedOrderMovesThroughApprovalPaymentAndExecution` (OS/Billing/Execução por ElasticMQ; consulta de pagamento simulada) | Coberto em um cenário local |
| Pagamento `expired`/`canceled` → liberação | `StockReservationFlowTests.ReservationResultUpdatesOrderWithoutOverselling` (Billing consulta order simulada, publica via ElasticMQ; OS/Execução liberam estoque e concluem) | Coberto de Billing até Execução/OS |
| Recusa após pagamento → revisão administrativa | `ExecutionRejectionTests.RejectionMovesPaidOrderToManualReviewWithoutFinalizing` | Coberto |
| Pedido → estorno confirmado → OS cancelada | `RefundFlowTests.RefundRequestCrossesQueuesAndConfirmationCancelsOrder`; `RefundOrderProcessorTests.RefundIsConfirmedOnlyByGetAndDoesNotRepeatAmbiguousPost` | Coberto com provedor simulado |
| POST ambíguo → sem repetição → reconciliação | `RefundOrderProcessorTests.RefundIsConfirmedOnlyByGetAndDoesNotRepeatAmbiguousPost`; `RefundReconciliationHttpTests.InternalRouteRequiresTokenAndConfirmsRefundWithGetOnly` | Coberto com provedor simulado |
| Falha de envio da Outbox → retry | `OpenedEventFlowTests.OpenedOrderMovesThroughApprovalPaymentAndExecution` (falha uma vez, verifica pendência/tentativas, reenvia) | Coberto localmente |
| Reentrega de `PaymentConfirmed` → sem novo consumo | `OpenedEventFlowTests.OpenedOrderMovesThroughApprovalPaymentAndExecution` (reenvia mesmo `eventId` por fila isolada, Inbox rejeita) | Coberto localmente |
| Mensagem inválida → DLQ | `DeadLetterFlowTests.InvalidMessageMovesToDeadLetterQueue` (OS, Billing e Execução) | Coberto em ElasticMQ; AWS pendente |
| Dois publicadores OS → uma seleção da Outbox | `OutboxConcurrencyTests.TwoDispatchersDoNotPublishTheSamePendingMessage` | Coberto em PostgreSQL local; outras Outboxes pendentes |

Execute os testes com ElasticMQ e DynamoDB Local:

```powershell
$env:MESSAGING_SQS_ENDPOINT='http://localhost:9324'
$env:BILLING_DYNAMO_ENDPOINT='http://localhost:8000'
.\.dotnet_home\sdk10\dotnet.exe test tests/Oficina.Messaging.Local.Tests --no-restore
.\.dotnet_home\sdk10\dotnet.exe test services/Oficina.Billing.Dynamo.Tests --no-restore
.\.dotnet_home\sdk10\dotnet.exe test tests/OficinaMvp.Integration.Tests --no-restore
docker compose -f compose.execution.yml up -d execution-db
docker compose -f compose.execution.yml exec -T execution-db psql -U execution -d oficina_execution -c 'CREATE SCHEMA IF NOT EXISTS os_outbox_retry_validation_20261114'
$env:OS_POSTGRES_CONNECTION_STRING='Host=localhost;Port=5433;Database=oficina_execution;Username=execution;Password=execution-local-password;Search Path=os_outbox_retry_validation_20261114'
.\.dotnet_home\sdk10\dotnet.exe test tests/OficinaMvp.Postgres.Tests --no-restore
```

O teste PostgreSQL aplica as migrations no schema isolado antes de disputar a linha da Outbox. O workflow [phase4-local.yml](../../.github/workflows/phase4-local.yml) prepara ElasticMQ, DynamoDB Local e PostgreSQL em um runner hospedado pelo GitHub, executa Billing/Execução/Saga/concorrência e publica arquivos TRX. O Compose combinado e o teste passaram localmente, mas ainda não há execução remota nem relatório de CI.

Antes de marcar BDD como completo, ampliar as especificações executáveis para os demais caminhos de integração, gerar evidência de execução no CI e confirmar se a avaliação exige que todo `saga-fase4.feature` seja executado diretamente. Testes com Mercado Pago real dependem de credenciais de testes.
