# Oficina Billing (extração em andamento)

Serviço independente para orçamento versionado em DynamoDB. Usa valores monetários em centavos inteiros; decisões são escritas de forma condicional. Ainda **não** implementa cobrança, webhook, estorno Mercado Pago ou SQS.

Da raiz do workspace:

```powershell
docker compose -f compose.billing.yml up -d --build
Invoke-WebRequest http://localhost:8082/health/ready
dotnet test services/Oficina.Billing.Tests/Oficina.Billing.Tests.csproj
```

Rotas iniciais com `X-Internal-Token`: `POST /internal/budgets`, `GET /internal/budgets/{workOrderId}/{version}` e `POST /internal/budgets/{workOrderId}/{version}/decision`. Swagger apenas em Development: `http://localhost:8082/swagger`. DynamoDB Local usa memória e perde dados ao parar. Em AWS, a tabela será criada por Terraform e o SDK usará identidade temporária do workload; nenhuma chave AWS deve ser gravada em configuração.
