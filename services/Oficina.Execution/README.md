# Oficina Execution (extração em andamento)

Serviço independente de catálogo técnico, cotação de serviços/peças e reserva de estoque. Reaproveita as regras `RepairService` e `PartSupply` da solução original, com persistência PostgreSQL própria. A OS consome a cotação HTTP; quando `Messaging:CommandsQueueUrl` está configurado, Execução recebe `ReserveStockRequested`, `ReleaseStockRequested`, `ConsumeStockRequested` e `ExecutionRequested` por SQS. Cada comando grava Inbox e resultado na Outbox junto com seus efeitos SQL. `Messaging:OsEventsQueueUrl` publica `StockReserved`/`StockReservationRejected`/`StockReleased`/`StockConsumed`/`ExecutionStarted`/`ExecutionFinished`/`ExecutionRejected`. O reparo iniciado aparece em `GET /internal/executions`; `POST /internal/executions/{reservationId}/finish` registra a conclusão e `POST /internal/executions/{reservationId}/reject` registra a recusa com motivo. Ambos gravam estado e evento idempotentemente na mesma transação. Os endpoints internos exigem `X-Internal-Token`. O estorno após recusa e o deploy Kubernetes ainda não foram concluídos.

## Local

Da raiz do workspace:

```powershell
docker compose -f compose.execution.yml up -d --build
Invoke-WebRequest http://localhost:8081/health/ready
```

As rotas `/internal/*` exigem `X-Internal-Token`, configurado apenas para desenvolvimento no Compose. `POST /internal/catalog/quote`, `POST /internal/stock/reservations` e `POST /internal/stock/reservations/{id}/release` são os contratos iniciais. O catálogo pode ser cadastrado por `POST /internal/catalog/services` e `/internal/catalog/parts`. Swagger local: `http://localhost:8081/swagger`.

Para testar:

```powershell
dotnet test services/Oficina.Execution.Tests/Oficina.Execution.Tests.csproj
```

O teste local de ida e volta da reserva usa ElasticMQ e bancos SQLite em memória:

```powershell
docker compose -f compose.messaging.yml up -d elasticmq
docker compose -f compose.billing.yml up -d dynamodb-local
$env:MESSAGING_SQS_ENDPOINT='http://127.0.0.1:9324'
$env:BILLING_DYNAMO_ENDPOINT='http://127.0.0.1:8000'
dotnet test tests/Oficina.Messaging.Local.Tests/Oficina.Messaging.Local.Tests.csproj
```

O URL DynamoDB é usado pelo cenário de orçamento do mesmo projeto de testes. `Messaging:SqsServiceUrl` é apenas para o emulador; na AWS o SDK usa a identidade temporária do workload.

Os testes de concorrência usam PostgreSQL de verdade, exposto pelo Compose somente em `127.0.0.1:5433`:

```powershell
docker compose -f compose.execution.yml up -d execution-db
$env:EXECUTION_POSTGRES_CONNECTION_STRING='Host=127.0.0.1;Port=5433;Database=oficina_execution;Username=execution;Password=execution-local-password'
dotnet test services/Oficina.Execution.Postgres.Tests/Oficina.Execution.Postgres.Tests.csproj
```

Cada teste cria identificadores exclusivos e remove seus registros ao terminar. Uma atualização concorrente do mesmo saldo recebe conflito; a transação não grava a reserva perdedora. O chamador pode repetir com a mesma chave de reserva para consultar o resultado idempotente.

O banco local no Compose usa `EnsureCreated` nesta fase. A migration PostgreSQL está versionada em `Persistence/Migrations` para banco novo de produção; ao separar o repositório, adaptar deploy para job de migration único antes de múltiplas réplicas. A configuração Production exige `InternalToken` por variável/segredo e conexão própria; não usar as credenciais de desenvolvimento.
