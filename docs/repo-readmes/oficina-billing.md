# oficina-billing

Serviço .NET 10 de orçamento, decisão do cliente, tentativa de pagamento e reconciliação/estorno. Possui tabela DynamoDB própria com escritas condicionais, Inbox/Outbox e IDs idempotentes. O adaptador Mercado Pago usa Orders de testes; sem credenciais externas, apenas testes com provedor simulado são comprovados.

```mermaid
flowchart LR
  OS[OS] <--> SQS[SQS e DLQ]
  SQS <--> API[Billing HTTP API e workers]
  API --> Dynamo[(DynamoDB Billing)]
  API -. sandbox .-> MP[Mercado Pago]
```

## Executar e testar

Instale o SDK indicado em `global.json` e inicie DynamoDB Local na porta 8000. `appsettings.Development.json` usa essa URL e cria a tabela local se faltar:

```powershell
docker run --rm -p 8000:8000 amazon/dynamodb-local:latest
```

Em outro terminal:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:8080'
dotnet run --project services/Oficina.Billing
```

Acesse [Swagger local](http://localhost:8080/swagger) e [healthcheck](http://localhost:8080/health/ready). O grupo `/internal` exige `X-Internal-Token` e expõe orçamento, decisão e consultas de tentativa. `POST /webhooks/mercado-pago/orders` é usado quando o provedor de testes e assinatura estiverem configurados. O Swagger só é exposto em Development.

```powershell
dotnet test services/Oficina.Billing.Tests -c Release
$env:BILLING_DYNAMO_ENDPOINT = 'http://localhost:8000'
$env:MESSAGING_SQS_ENDPOINT = 'http://localhost:9324'
dotnet test services/Oficina.Billing.Dynamo.Tests -c Release
docker build -f services/Oficina.Billing/Dockerfile -t oficina-billing:local .
kubectl kustomize k8s/services/billing
```

Para a suíte de integração, inicie também ElasticMQ na porta 9324 em outro terminal (`docker run --rm -p 9324:9324 softwaremill/elasticmq-native:latest`). Os testes usam `BILLING_DYNAMO_ENDPOINT` e `MESSAGING_SQS_ENDPOINT`. `RefundReconciliation.feature` executa dois cenários Gherkin de estorno com provedor HTTP simulado; a suíte de integração passou 17/17 localmente. O workflow `billing-ci` inicia os emuladores, executa as duas suítes, exige cobertura de 80% e, com OIDC configurado, publica imagem ECR e solicita rollout K3s por SSM.

## Deploy

O overlay `k8s/services/billing` inclui Deployment, Service, ServiceAccount, HPA, ConfigMap e probes. `REPLACE_WITH_*` e Secrets são preenchidos pelo bootstrap; tokens Mercado Pago são externos ao Git. `oficina-data-infra` provê DynamoDB/filas e `oficina-k8s-infra` provê o cluster. Consulte a [arquitetura completa](docs/architecture-phase4.md) e a [reconciliação de estorno](docs/runbooks/refund-reconciliation.md). Nenhum deploy AWS nem pagamento real foi comprovado até a execução autorizada.
