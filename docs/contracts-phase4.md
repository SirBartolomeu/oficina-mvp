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
| `BudgetReady` | Billing → OS | `budgetId`, `budgetVersion`, `totalCents`, `currency` | OS muda para aguardando aprovação |
| `BudgetApproved` / `BudgetRefused` | Billing → OS | `budgetId`, `budgetVersion`, `decisionId`, motivo opcional | OS aceita somente decisão da versão corrente; recusa encerra/revisa tentativa |
| `ReserveStockRequested` | OS → Execução | `reservationId`, itens/quantidades, `expiresAtUtc` | Reserva atômica, idempotente e condicionada a saldo; emite confirmado/recusado |
| `StockReserved` / `StockReservationRejected` | Execução → OS | `reservationId`, `orderVersion`, motivo opcional | Confirmado libera cobrança; recusado volta para revisão |
| `CreatePaymentRequested` | OS → Billing | `paymentAttemptId`, `budgetId`, `budgetVersion`, `totalCents`, `currency` | Billing cria tentativa com chave estável de idempotência Mercado Pago |
| `PaymentConfirmed` / `PaymentRejected` | Billing → OS | `paymentAttemptId`, `providerOrderId`, `totalCents`, `currency` | Confirmação exige consulta ao provedor; rejeição libera reserva após reconciliação |
| `ReleaseStockRequested` | OS → Execução | `reservationId`, `reason` | Liberação idempotente; não libera após consumo irreversível |
| `RefundPaymentRequested` | OS → Billing | `paymentAttemptId`, `reason` | Estorno idempotente, resultado confirmado pelo provedor antes de concluir compensação |
| `ExecutionRequested` | OS → Execução | `executionId`, `reservationId`, `paymentAttemptId` | Só iniciar depois de pagamento confirmado e reserva válida |
| `ExecutionStarted` / `ExecutionFinished` / `ExecutionRejected` | Execução → OS | `executionId`, timestamp, motivo opcional | Atualiza status/histórico; falha antes do reparo inicia compensação |

Os nomes são contratos de integração, não confirmação de serviços já implementados. Para SQS, usar filas por consumidor com DLQ, visibilidade adequada e idempotência; a ordem é conferida por versão e estado. Outbox transacional em SQL/DynamoDB impede perda entre persistência e publicação. O timeout de cobrança entra em reconciliação, nunca em nova cobrança automática. O webhook Mercado Pago apenas sinaliza alteração; Billing consulta a Orders API para confirmar estado, valor e referência antes de publicar `PaymentConfirmed`.

No evento raiz `WorkOrderOpened`, a OS gera `causationId` para a requisição de abertura e usa `workOrderId` como `correlationId`. A Outbox SQL armazena o envelope JSON na mesma transação que grava a OS. O publicador SQS e o consumidor Billing estão implementados para esse evento; o Billing grava orçamento e Inbox em uma transação DynamoDB. Eventos de resposta e demais comandos da Saga ainda não estão implementados.

## REST síncrono necessário

- `POST /internal/catalog/quote`: OS envia IDs/quantidades; Execução devolve snapshot de preço/nome e disponibilidade indicativa. Falha impede abertura antes de persistir efeitos.
- `POST /internal/customers/lookup`: Lambda envia CPF no corpo e consulta OS via rede/identidade interna para existência e status, sem acesso direto ao PostgreSQL. Não registrar corpo ou CPF em URL/log.
- APIs públicas de OS, Billing e Execução têm Swagger próprio e autorização por função. Operações mutáveis da Saga usam mensagens; REST interno serve apenas consulta pontual.

## Critérios de contrato

1. Cada serviço aceita `schemaVersion=1` e ignora campos adicionais compatíveis; quebra de contrato requer versão nova.
2. Duplicação do mesmo `eventId`, webhook duplicado, mensagem antiga e reordenação não alteram saldo, pagamento ou status duas vezes.
3. `totalCents` corresponde à soma das linhas do orçamento versionado; moeda sempre explícita.
4. `workOrderId`, `branchId`, `correlationId` e `causationId` aparecem em logs JSON e traces, sem dados pessoais.
