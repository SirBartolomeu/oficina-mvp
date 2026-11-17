# Oficina Billing (extração em andamento)

## Checkout Pro via Orders API — somente testes

O perfil `compose.checkout-test.yml` seleciona Checkout Pro no worker existente da
Saga. Ele cria uma order `online/manual`, persiste `checkoutUrl` e devolve esse
campo em `GET /internal/payments/{paymentAttemptId}`. O valor vem do orçamento
aprovado. A página `/dev/checkout`, disponível apenas em Development, permite
consultar a tentativa com a chave interna e abrir o checkout de teste.

Configure no ambiente do processo `MP_TEST_ACCESS_TOKEN` e
`MP_TEST_WEBHOOK_SECRET`, obtidos na aba **Teste** da aplicação. Não coloque
segredos no Git ou no chat. Opcionalmente, defina `MP_TEST_RETURN_URL` com uma URL
HTTPS pública terminada em `/checkout/return`.

```powershell
docker compose -f compose.billing.yml -f compose.checkout-test.yml up -d --build
```

Antes de criar ou estornar uma order, o cliente verifica `/users/me` e exige
`tags` contendo `test_user` e `site_id=MLB`. Se a API não comprovar essa identidade,
a operação é bloqueada. O modo Checkout Pro exige `TestMode=true`, recusa o ambiente
ASP.NET Production e só consulta/redireciona orders `ORDTST`. O perfil mantém o
worker de estorno desligado. Não há fallback para credenciais de produção.

Cadastre **Order (Mercado Pago)** no ambiente **Teste**, apontando para a URL HTTPS
pública de `/webhooks/mercado-pago/orders`. O receptor valida assinatura e
`live_mode=false`, grava a entrega em DynamoDB e responde antes da consulta ao
provedor. O worker reconcilia a entrega com a tentativa persistida e publica
`PaymentConfirmed` ou `PaymentRejected` pela Outbox. Duplicatas não duplicam o
evento. A recusa `failed` libera estoque pela Saga, assim como `canceled`/`expired`.
Há recuperação manual por `POST /internal/payments/{paymentAttemptId}/reconcile`.
Parâmetros de retorno do navegador nunca confirmam pagamentos.

As chamadas Mercado Pago nos testes automatizados são simuladas; DynamoDB e SQS
usam emuladores locais. A aplicação, credenciais, URL HTTPS e registro remoto do
webhook ainda dependem de acesso à conta. Consulte o estado em
[preparação de testes](../../docs/mercadopago-checkout-pro-testes.md).

Serviço independente para orçamento versionado em DynamoDB. Usa valores monetários em centavos inteiros; decisões são escritas de forma condicional. Consome `WorkOrderOpened` de uma fila SQS quando `Messaging:WorkOrdersQueueUrl` é configurado. Grava orçamento, Inbox e Outbox `BudgetReady` na mesma transação DynamoDB. A rota de decisão grava `BudgetApproved` ou `BudgetRefused` junto com a alteração de status, também em uma transação. Após `StockReserved`, consome `CreatePaymentRequested` e grava tentativa + Inbox atomicamente. Publica eventos quando `Messaging:OsEventsQueueUrl` é configurado. Cada mensagem da Outbox recebe uma reivindicação condicional de dois minutos antes do envio; só o dono pode removê-la. Falha no envio libera a reivindicação; queda do processo permite retomada após o prazo. Uma queda entre SQS e a remoção ainda pode duplicar a mensagem, por isso o consumidor mantém Inbox idempotente. Com credenciais de teste configuradas, o worker cria a order QR dinâmica usando `paymentAttemptId` como chave de idempotência e grava order/QR em transação condicional. Um webhook de order assinado aciona consulta ao provedor; pagamento processado e coerente grava `PaymentConfirmed`, enquanto order expirada/cancelada sem pagamento grava `PaymentRejected` na Outbox. A OS inicia a liberação de estoque após recusa. `RefundPaymentRequested` grava Inbox, estado `RefundRequested` e índice de pendências em transação DynamoDB. Com `MercadoPago:EnableRefundWorker=true` e credenciais de teste, o worker consulta a order, marca `RefundSubmitting` antes do POST de estorno total com `refundRequestId` como chave e só grava `Refunded`/`RefundConfirmed` após GET comprovar o estorno. Se o POST falhar ou a resposta for ambígua, o worker segue consultando a order, sem repetir o POST; se não houver confirmação, é necessária reconciliação operacional. O worker fica desligado por padrão. Nenhuma cobrança é efetuada apenas pela criação da tentativa.

Para um estorno ambíguo, `POST /internal/payments/{paymentAttemptId}/reconcile-refund` consulta a order sem repetir o POST. A rota só existe com credenciais Mercado Pago configuradas e usa o mesmo `X-Internal-Token` das outras rotas internas. Procedimento: [reconciliação de estorno](../../docs/runbooks/refund-reconciliation.md).

Da raiz do workspace:

```powershell
docker compose -f compose.billing.yml up -d --build
Invoke-WebRequest http://localhost:8082/health/ready
dotnet test services/Oficina.Billing.Tests/Oficina.Billing.Tests.csproj
```

Teste transacional com DynamoDB Local em memória:

```powershell
docker compose -f compose.billing.yml up -d dynamodb-local
$env:BILLING_DYNAMO_ENDPOINT='http://127.0.0.1:8000'
dotnet test services/Oficina.Billing.Dynamo.Tests/Oficina.Billing.Dynamo.Tests.csproj
```

Fluxo local OS → SQS compatível → Billing → SQS compatível → OS:

```powershell
docker compose -f compose.messaging.yml up -d elasticmq
$env:MESSAGING_SQS_ENDPOINT='http://127.0.0.1:9324'
$env:BILLING_DYNAMO_ENDPOINT='http://127.0.0.1:8000'
dotnet test tests/Oficina.Messaging.Local.Tests/Oficina.Messaging.Local.Tests.csproj
```

O teste cria filas e tabela com nomes únicos no emulador local e verifica orçamento, duplicata, `BudgetReady`, `BudgetApproved`, pedido de reserva, pedido de pagamento e status da OS. Em execução real, configure `Messaging:WorkOrdersQueueUrl` e `Messaging:OsEventsQueueUrl`; use `Messaging:SqsServiceUrl` apenas para o emulador. Sem URL de serviço, o SDK usa as credenciais temporárias do ambiente AWS.

Para ativar o worker de QR dinâmico, configure `MercadoPago__AccessToken` e `MercadoPago__ExternalPosId` com o Access Token de teste e o identificador de um caixa previamente criado. Configure também `MercadoPago__WebhookSecret` para habilitar `POST /webhooks/mercado-pago/orders` e cadastre sua URL HTTPS no painel do provedor. Não grave segredos no repositório. A resposta da order deve corresponder ao valor e à referência da tentativa. `GET /internal/payments/{paymentAttemptId}` devolve estado, identificador da order e QR após a criação. Falha HTTP mantém a tentativa pendente para nova chamada com a mesma chave; tentativa pendente há mais de 23 horas vira `ManualReview` sem chamar o provedor. O webhook compara `x-signature` com HMAC-SHA256 do identificador em `data.id`, `x-request-id` e `ts`; depois confere o identificador no corpo e consulta a order por API. Os testes usam HTTP simulado; nenhum teste atual chama o Mercado Pago.

Rotas iniciais com `X-Internal-Token`: `POST /internal/budgets`, `GET /internal/budgets/{workOrderId}/{version}`, `POST /internal/budgets/{workOrderId}/{version}/decision` e `GET /internal/payments/{paymentAttemptId}`. Swagger apenas em Development: `http://localhost:8082/swagger`. DynamoDB Local usa memória e perde dados ao parar. Em AWS, a tabela será criada por Terraform e o SDK usará identidade temporária do workload; nenhuma chave AWS deve ser gravada em configuração.
