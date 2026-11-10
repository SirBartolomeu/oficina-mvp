# Oficina Execution (extração em andamento)

Serviço independente de catálogo técnico, cotação de serviços/peças e reserva de estoque. Reaproveita as regras `RepairService` e `PartSupply` da solução original, com persistência PostgreSQL própria. A OS já pode consumir a cotação HTTP; ainda não há consumo de SQS, fila de reparos ou deploy Kubernetes. Não usar como evidência de Fase 4 completa.

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

Os testes de concorrência usam PostgreSQL de verdade, exposto pelo Compose somente em `127.0.0.1:5433`:

```powershell
docker compose -f compose.execution.yml up -d execution-db
$env:EXECUTION_POSTGRES_CONNECTION_STRING='Host=127.0.0.1;Port=5433;Database=oficina_execution;Username=execution;Password=execution-local-password'
dotnet test services/Oficina.Execution.Postgres.Tests/Oficina.Execution.Postgres.Tests.csproj
```

Cada teste cria identificadores exclusivos e remove seus registros ao terminar. Uma atualização concorrente do mesmo saldo recebe conflito; a transação não grava a reserva perdedora. O chamador pode repetir com a mesma chave de reserva para consultar o resultado idempotente.

O banco local no Compose usa `EnsureCreated` nesta fase. A migration PostgreSQL está versionada em `Persistence/Migrations` para banco novo de produção; ao separar o repositório, adaptar deploy para job de migration único antes de múltiplas réplicas. A configuração Production exige `InternalToken` por variável/segredo e conexão própria; não usar as credenciais de desenvolvimento.
