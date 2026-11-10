# Oficina Billing (extração em andamento)

Serviço independente para orçamento versionado em DynamoDB. Usa valores monetários em centavos inteiros; decisões são escritas de forma condicional. Consome `WorkOrderOpened` de uma fila SQS quando `Messaging:WorkOrdersQueueUrl` é configurado. Grava orçamento e Inbox na mesma transação DynamoDB. Ainda **não** implementa cobrança, webhook, estorno Mercado Pago ou resposta `BudgetReady` à OS.

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

Fluxo local Outbox → SQS compatível → worker Billing → DynamoDB:

```powershell
docker compose -f compose.messaging.yml up -d elasticmq
$env:MESSAGING_SQS_ENDPOINT='http://127.0.0.1:9324'
$env:BILLING_DYNAMO_ENDPOINT='http://127.0.0.1:8000'
dotnet test tests/Oficina.Messaging.Local.Tests/Oficina.Messaging.Local.Tests.csproj
```

O teste cria fila e tabela com nomes únicos no emulador local. Em execução real, configure `Messaging:WorkOrdersQueueUrl` e opcionalmente `Messaging:SqsServiceUrl` apenas para o emulador; sem URL de serviço, o SDK usa as credenciais temporárias do ambiente AWS.

Rotas iniciais com `X-Internal-Token`: `POST /internal/budgets`, `GET /internal/budgets/{workOrderId}/{version}` e `POST /internal/budgets/{workOrderId}/{version}/decision`. Swagger apenas em Development: `http://localhost:8082/swagger`. DynamoDB Local usa memória e perde dados ao parar. Em AWS, a tabela será criada por Terraform e o SDK usará identidade temporária do workload; nenhuma chave AWS deve ser gravada em configuração.
