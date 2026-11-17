# Arquitetura da demonstração Fase 4

## AWS preparado pelo Terraform

```mermaid
flowchart LR
  Cliente[Cliente] --> APIGW[HTTP API /auth/token]
  APIGW --> Auth[Lambda Auth .NET 10]
  Auth --> VPCE[Endpoint privado Secrets Manager]
  VPCE --> Secrets[(Segredo da OS)]
  Auth -- NodePort 30080 privado --> OS[OS + Saga no K3s]
  Operador[Operador por Session Manager] --> K3s[EC2 / K3s]
  K3s --> OS
  K3s --> Exec[Execução]
  K3s --> Billing[Billing]
  OS --> OSDB[(RDS: oficina_os)]
  Exec --> ExecDB[(RDS: oficina_execution)]
  Billing --> Dynamo[(DynamoDB Billing)]
  OS <--> SQS[SQS + DLQ]
  Exec <--> SQS
  Billing <--> SQS
  Billing -. testes pendentes .-> MP[Mercado Pago sandbox]
  GitHub[Actions por repositório] -- OIDC --> IAM[IAM]
  IAM --> ECR[ECR]
  IAM -- SSM deploy --> K3s
  OS --> Otel[Collector / Prometheus / Grafana]
  Exec --> Otel
  Billing --> Otel
```

OS é dona do pedido e orquestra a Saga. Execução é dona de catálogo, estoque e reparo. Billing é dono de orçamento e pagamento. Cada serviço grava seus próprios dados; eventos SQS atravessam Outbox/Inbox, com idempotência e compensações. A Lambda consulta a OS por rede privada e lê apenas o segredo da OS via endpoint privado. O security group não abre SSH nem a API da OS ao público. O smoke AWS usa Session Manager e `port-forward` no nó para criar/consultar a OS; somente a HTTP API da Lambda tem URL pública neste desenho.

O webhook real do Mercado Pago exige ingresso HTTPS público para Billing e credenciais de teste; ambos continuam pendentes. Acesso remoto a JWT de cliente e tracking também depende de publicar a rota da OS com TLS. Os manifests e Terraform foram validados localmente, mas a implantação AWS ainda precisa de `plan` revisado, `apply` autorizado e smoke real.

## Execução local comprovada

```mermaid
flowchart LR
  Cliente --> Traefik[Traefik HTTPS local]
  Traefik --> Adaptador[Adaptador local do handler Auth]
  Traefik --> OS[OS .NET]
  OS --> OSDB[(PostgreSQL OS)]
  OS <--> ElasticMQ[ElasticMQ]
  ElasticMQ <--> Exec[Execução .NET]
  ElasticMQ <--> Billing[Billing .NET]
  Exec --> ExecDB[(PostgreSQL Execução)]
  Billing --> Dynamo[(DynamoDB Local)]
  OS --> OTel[Collector]
  Exec --> OTel
  Billing --> OTel
  OTel --> Grafana[Prometheus / Grafana]
```

`scripts/demo-fase4.ps1` verifica localmente abertura de OS, cotação Execução, orçamento Billing, CPF → JWT → tracking, trace distribuído e dashboard. O pagamento no demo local usa provedor simulado. A [matriz de evidências](evidence/phase4-status.md) distingue preparação, execução local e comprovação remota.
