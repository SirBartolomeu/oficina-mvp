# Oficina Execution (extração em andamento)

Serviço independente de catálogo técnico, cotação de serviços/peças e reserva de estoque. Reaproveita as regras `RepairService` e `PartSupply` da solução original, com persistência PostgreSQL própria. Ainda não há consumo de SQS, fila de reparos, deploy Kubernetes ou integração com OS; não usar como evidência de Fase 4 completa.

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

O banco local no Compose usa `EnsureCreated` nesta fase. A migration PostgreSQL está versionada em `Persistence/Migrations` para banco novo de produção; ao separar o repositório, adaptar deploy para job de migration único antes de múltiplas réplicas. A configuração Production exige `InternalToken` por variável/segredo e conexão própria; não usar as credenciais de desenvolvimento.
