# Contratos de integração propostos — Fase 4

Estado: versão 1 do contrato para implementação. Donos dos dados: OS, Execução e Billing. Nenhum serviço lê o banco de outro. O contrato é JSON versionado e será copiado para os três repositórios com testes de compatibilidade; não criar biblioteca de entidades compartilhadas.

## Envelope comum

```json
{
  "eventId": "uuid",
  "eventType": "WorkOrderOpened",
  "schemaVersion": 1,
  "correlationId": "uuid",
  "causationId": "uuid",
  "workOrderId": "uuid",
  "branchId": "sp-centro",
  "occurredAtUtc": "2026-11-10T12:00:00Z",
  "payload": {}
}
```

`branchId` é opcional enquanto não houver cadastro de filiais; quando informado, deve ter até 64 caracteres e ser propagado sem alteração. Nunca incluir CPF/CNPJ, JWT, credencial Mercado Pago ou dados de cartão nas mensagens. `eventId` identifica a entrega lógica; o consumidor registra Inbox antes de aplicar o efeito. `correlationId` percorre toda a Saga. `causationId` aponta para o evento/comando que gerou a mensagem atual. Cada mudança de estado leva a versão monotônica da OS (`orderVersion`) ou do agregado local para rejeitar mensagens antigas.

## Eventos e comandos (v1)

| Tipo | Emissor → receptor | Payload mínimo | Efeito / compensação |
|---|---|---|---|
| `WorkOrderOpened` | OS → Billing | `orderVersion`, `lines` (`kind`, `itemId`, `name`, `quantity`, `unitPriceCents`), `totalCents`, `currency=BRL`, `quotedAtUtc` | Criar orçamento versão 1, sem recalcular a partir de preço atual |
| `BudgetReady` | Billing → OS | `budgetId`, `budgetVersion`, `totalCents`, `currency=BRL` | OS confere valor/filial e muda para aguardando aprovação; duplicata mantém o estado |
| `BudgetApproved` / `BudgetRefused` | Billing → OS | `budgetId`, `budgetVersion`, `decisionId`, motivo opcional | OS aceita somente decisão da versão corrente; aprovação solicita reserva; recusa mantém a OS para revisão |
| `ReserveStockRequested` | OS → Execução | `reservationId`, `orderVersion`, peças/quantidades, `expiresAtUtc` | Reserva atômica, idempotente e condicionada a saldo; emite confirmado/recusado |
| `StockReserved` / `StockReservationRejected` | Execução → OS | `reservationId`, `orderVersion`, motivo opcional | Confirmado muda o processo para aguardar pagamento; recusado cancela a tentativa antes da execução |
| `CreatePaymentRequested` | OS → Billing | `paymentAttemptId`, `budgetId`, `budgetVersion`, `totalCents`, `currency` | Billing valida orçamento aprovado e persiste tentativa com Inbox; `paymentAttemptId` será a chave estável de idempotência Mercado Pago |
| `PaymentConfirmed` / `PaymentRejected` | Billing → OS | `paymentAttemptId`, `providerOrderId`, `totalCents`, `currency`; recusa inclui `reason` | Confirmação exige consulta ao provedor; order expirada/cancelada sem pagamento inicia liberação da reserva |
| `ConsumeStockRequested` / `StockConsumed` | OS → Execução → OS | `reservationId` | Após pagamento, Execução consome a reserva; a OS solicita o início somente depois da confirmação |
| `ReleaseStockRequested` / `StockReleased` | OS → Execução → OS | `reservationId`, `reason` no comando | Liberação idempotente; OS cancela a tentativa somente após confirmação; não libera após consumo irreversível |
| `RefundPaymentRequested` | OS → Billing | `paymentAttemptId`, `refundRequestId`, `reason` | Decisão administrativa após revisão; Billing aceita uma solicitação por pagamento confirmado e mantém pendência para reconciliação |
| `RefundConfirmed` | Billing → OS | `paymentAttemptId`, `refundRequestId`, `providerOrderId`, `totalCents`, `currency=BRL` | Só depois de GET comprovar order e pagamento estornados; OS confere IDs/valor/filial e encerra como cancelada |
| `ExecutionRequested` | OS → Execução | `executionId`, `reservationId`, `paymentAttemptId` | Só iniciar depois de pagamento confirmado e reserva consumida; `executionId=workOrderId` nesta versão |
| `ExecutionStarted` / `ExecutionFinished` / `ExecutionRejected` | Execução → OS | `executionId`, `reservationId`, `paymentAttemptId`; recusa inclui `reason` | Início e conclusão atualizam a OS; recusa paga exige revisão manual até o estorno ser implementado |

`ConsumeStockRequested`/`StockConsumed` confirmam a reserva consumida. A OS então enfileira `ExecutionRequested`; a Execução confere a reserva e publica `ExecutionStarted`, que inicia a OS. A conclusão interna da Execução grava `ExecutionFinished` na Outbox e a OS finaliza ao consumi-lo. A recusa interna grava `ExecutionRejected` e a OS entra em `ManualReview`; uma decisão administrativa cria `RefundPaymentRequested`. Billing registra o pedido e um worker opt-in consulta a order, envia o POST de estorno uma vez com chave estável e só publica `RefundConfirmed` após GET confirmar o estorno. Após resultado ambíguo, permanece em `RefundSubmitting` e consulta novamente sem repetir o POST; exige intervenção se o provedor não confirmar. A OS consome a confirmação com Inbox e muda para `Canceled`. Para SQS, usar filas por consumidor com DLQ, visibilidade adequada e idempotência; a ordem é conferida por versão e estado. Outbox transacional em SQL/DynamoDB impede perda entre persistência e publicação. O timeout de cobrança entra em reconciliação, nunca em nova cobrança automática. O webhook Mercado Pago apenas sinaliza alteração; Billing consulta a Orders API para confirmar estado, valor e referência antes de publicar `PaymentConfirmed`.

No evento raiz `WorkOrderOpened`, a OS gera `causationId` para a requisição de abertura e usa `workOrderId` como `correlationId`. A Outbox SQL armazena o envelope JSON na mesma transação que grava a OS. O Billing grava orçamento, Inbox e `BudgetReady` pendente em uma transação DynamoDB. Seu publicador envia a resposta por SQS; a OS registra Inbox e transição de estado na mesma transação SQL. O `budgetId` da versão criada a partir desse evento usa o `eventId` de `WorkOrderOpened`, garantindo identidade estável em reentregas.

A decisão de aprovação ou recusa é gravada pelo Billing junto com seu evento na mesma transação DynamoDB. No modo distribuído, a OS não aceita aprovação direta pelo endpoint legado. Ao consumir `BudgetApproved`, a OS grava `ReserveStockRequested` na Outbox SQL junto com o estado `AwaitingStockReservation`; `BudgetRefused` registra a recusa para revisão. Execução valida prazo e saldo, grava reserva, Inbox e resultado na mesma transação; a OS valida `reservationId`, versão e filial antes de aceitar `StockReserved` ou `StockReservationRejected`. A recusa da reserva cancela a tentativa sem iniciar serviço ou cobrança. `StockReserved` cria uma tentativa estável na OS e enfileira `CreatePaymentRequested`; o Billing valida versão, valor e aprovação e grava tentativa + Inbox atomicamente. Com credenciais configuradas, o Billing cria a order QR. O webhook assinado aciona uma consulta `GET /v1/orders/{id}`; só uma order processada, com pagamento, referência e valor coerentes, gera `PaymentConfirmed` na Outbox. A OS valida tentativa/valor/filial, muda para `ReadyForExecution` e enfileira `ConsumeStockRequested`. Execução consome a reserva e publica `StockConsumed`; a OS enfileira `ExecutionRequested`. Execução valida a reserva consumida e publica `ExecutionStarted`; somente então a OS muda para `InExecution`. O endpoint interno da Execução finaliza o reparo uma vez, grava estado e `ExecutionFinished` na mesma transação, e a OS muda para `Finalized` ao consumi-lo. Order expirada ou cancelada sem pagamento gera `PaymentRejected`; a OS muda para `ReleasingStock` e enfileira `ReleaseStockRequested`. Execução devolve o saldo e publica `StockReleased`; só então a OS muda para `Canceled`. Recusa paga do reparo requer decisão administrativa; depois, Billing processa o estorno e a OS encerra ao receber `RefundConfirmed`. A reserva já consumida não é automaticamente devolvida ao estoque físico.

## REST síncrono necessário

- `POST /internal/catalog/quote`: OS envia IDs/quantidades; Execução devolve snapshot de preço/nome e disponibilidade indicativa. Falha impede abertura antes de persistir efeitos.
- `POST /internal/customers/lookup`: Lambda envia CPF no corpo e consulta OS via rede/identidade interna para existência e status, sem acesso direto ao PostgreSQL. Não registrar corpo ou CPF em URL/log.
- APIs públicas de OS, Billing e Execução têm Swagger próprio e autorização por função. Operações mutáveis da Saga usam mensagens; REST interno serve apenas consulta pontual.

## Critérios de contrato

1. Cada serviço aceita `schemaVersion=1` e ignora campos adicionais compatíveis; quebra de contrato requer versão nova.
2. Duplicação do mesmo `eventId`, webhook duplicado, mensagem antiga e reordenação não alteram saldo, pagamento ou status duas vezes.
3. `totalCents` corresponde à soma das linhas do orçamento versionado; moeda sempre explícita.
4. `workOrderId`, `branchId`, `correlationId` e `causationId` aparecem em logs JSON e traces, sem dados pessoais.
