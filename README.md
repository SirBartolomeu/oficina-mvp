# Oficina MVP - Tech Challenge Fase 2

API REST para gestao de oficina mecanica evoluida para a Fase 2 do Tech Challenge. A solucao usa `.NET 8`, `C#`, Clean Architecture, DDD tatico, JWT, EF Core, SQLite para uso local simples/testes, PostgreSQL para Docker/Kubernetes, Docker Compose, Kubernetes, Terraform local com kind, GitHub Actions, testes automatizados, cobertura critica e pipeline de vulnerabilidades.

## Objetivo

Entregar uma evolucao do sistema-base da Fase 1 sem reescrever o que ja funcionava, adicionando:

- separacao explicita em Clean Architecture;
- endpoint explicito de consulta de status da OS;
- decisao externa de orcamento por integracao;
- listagem de OS ativas por prioridade/status e mais antigas primeiro;
- notificacao de status por porta configuravel;
- PostgreSQL em Docker/Kubernetes;
- manifests Kubernetes com ConfigMap, Secret, probes, HPA, resources e persistencia;
- Terraform para cluster local kind;
- CI/CD com build, testes, imagem e deploy local em kind;
- correcao de achado SCA NuGet e container non-root.

## Requisitos Atendidos - Fase 2

| Requisito | Status | Evidencia |
|---|---|---|
| Clean Code e Clean Architecture | Atendido | Projetos `Domain`, `Application`, `Infrastructure` e `Api` |
| Testes automatizados dos fluxos criticos | Atendido | `55` testes de dominio e `11` de integracao |
| Abertura, status, listagem e decisao externa de OS | Atendido | Controllers de OS e integracao + testes de Fase 2 |
| Atualizacao/notificacao de status | Atendido | Porta `IWorkOrderStatusNotifier` com implementacoes log/SMTP |
| Dockerfile e Docker Compose | Atendido | API non-root, PostgreSQL, Mailpit e healthcheck |
| Kubernetes | Atendido | Manifests em `k8s/` com Deployment, Service, ConfigMap, Secret template, HPA, probes e PVC |
| Terraform | Atendido e validado | `init`, `validate` e `plan` executados em `infra/terraform` |
| CI/CD | Atendido | Workflows GitHub Actions para build/test, imagem e deploy kind |
| Seguranca | Atendido e validado | SCA NuGet, SAST Semgrep e scan Docker Scout executados com relatorio Markdown/PDF |

## Estrutura

```text
oficina-mvp/
  src/
    OficinaMvp.Domain/           # entidades, validadores e regras puras
    OficinaMvp.Application/      # casos de uso, DTOs e portas
    OficinaMvp.Infrastructure/   # EF Core, JWT, seed, notificacao e repositorios
    OficinaMvp.Api/              # controllers, middleware, DI, Swagger e health checks
  tests/
    OficinaMvp.Domain.Tests/
    OficinaMvp.Integration.Tests/
  docs/
    documentacao-ddd-tech-challenge.md
    diagramas/
  k8s/                           # manifests Kubernetes + kustomization
  infra/terraform/               # cluster local kind
  .github/workflows/             # CI/CD
  scripts/
  docker-compose.yml
```

## Arquitetura

Camadas:

- `Domain`: `WorkOrder`, `Customer`, `Vehicle`, `RepairService`, `PartSupply`, `WorkOrderStatusHistory`, `DocumentValidator`, `LicensePlateValidator`.
- `Application`: `WorkOrderApplicationService`, `WorkshopCatalogApplicationService`, contratos e portas `IWorkshopRepository`, `IWorkOrderStatusNotifier`.
- `Infrastructure`: `WorkshopDbContext`, `EfWorkshopRepository`, `TokenService`, `WorkshopDemoSeeder`, notificacao SMTP/log.
- `Api`: controllers REST, middleware global de excecoes, Swagger, JWT, health checks e composicao de DI.

Diagramas e documentacao DDD:

- `docs/documentacao-ddd-tech-challenge.md`
- `docs/diagramas/domain-storytelling-oficina-mvp.svg`
- `docs/diagramas/event-storming-oficina-mvp.svg`
- `docs/diagramas/event-storming-fase2-linha-do-tempo.svg`
- `docs/diagramas/agregados-fase2.svg`
- `docs/diagramas/arquitetura-fase2-kubernetes.svg`

## Banco de dados

A solucao suporta dois providers:

- `Sqlite`: execucao local simples e testes em memoria.
- `Postgres`: Docker Compose e Kubernetes.

Justificativa:

- SQLite continua util para MVP local, baixa friccao e testes rapidos.
- PostgreSQL foi adotado em Docker/Kubernetes porque SQLite com volume compartilhado nao combina com HPA e multiplas replicas da API.

Configuracao principal:

- `Database__Provider=Sqlite` ou `Postgres`
- `ConnectionStrings__DefaultConnection=...`
- `Database__UseMigrations=false` no ambiente local da entrega

## Seed de demo

A seed idempotente roda em `Development` ou quando `SeedDemoData=true`:

- cliente demo;
- veiculo demo;
- servicos demo;
- pecas/insumos demo.

Para desativar:

```powershell
$env:SeedDemoData = "false"
```

## Autenticacao e integracao

Credenciais administrativas locais:

- usuario: `admin`
- senha: `Admin@123`

Fluxo:

1. Chamar `POST /api/auth/token`.
2. Copiar `accessToken`.
3. Usar `Authorize` no Swagger com `Bearer {token}`.
4. Chamar rotas administrativas protegidas.

Token de integracao externa:

- Header: `X-Integration-Token`
- Endpoint: `POST /api/integrations/work-orders/{id}/budget-decision`

## Endpoints principais

Administrativos com JWT:

- `POST /api/auth/token`
- CRUD `/api/customers`
- CRUD `/api/vehicles`
- CRUD `/api/services`
- CRUD `/api/parts`
- `GET /api/work-orders`
- `GET /api/work-orders/{id}`
- `GET /api/work-orders/{id}/status`
- `POST /api/work-orders`
- `POST /api/work-orders/{id}/start-diagnosis`
- `POST /api/work-orders/{id}/send-budget`
- `POST /api/work-orders/{id}/approve-budget`
- `POST /api/work-orders/{id}/finalize`
- `POST /api/work-orders/{id}/deliver`
- `GET /api/work-orders/metrics/average-execution-time`

Publico:

- `GET /api/client/work-orders/{id}?document={cpfOuCnpj}`

Integracao externa:

- `POST /api/integrations/work-orders/{id}/budget-decision`

Health checks:

- `GET /health/live`
- `GET /health/ready`

## Collection das APIs

A collection oficial da entrega e o Swagger/OpenAPI gerado pela propria API:

- Swagger UI local: `http://localhost:5245/swagger`
- Swagger UI Docker ou Kubernetes via port-forward: `http://localhost:8080/swagger`
- OpenAPI JSON: `http://localhost:8080/swagger/v1/swagger.json`

## Pre-requisitos

Obrigatorios para desenvolvimento local:

- `.NET SDK` compativel com `global.json`
- PowerShell

Para Docker/Kubernetes:

- Docker Desktop com daemon ativo (`docker info` deve funcionar)
- `kubectl`
- Terraform, se for usar `infra/terraform`
- kind, se nao usar o provider/fluxo do Terraform para criar o cluster

Instalar Terraform no Windows, se necessario:

```powershell
winget install Hashicorp.Terraform --accept-source-agreements --accept-package-agreements
```

Se o comando `terraform` nao aparecer na mesma sessao, reinicie o terminal para atualizar o `PATH`.

## Rodar localmente sem Docker

```powershell
dotnet restore .\OficinaMvp.sln
dotnet run --project .\src\OficinaMvp.Api --launch-profile http
```

URLs:

- API: `http://localhost:5245`
- Swagger: `http://localhost:5245/swagger`

## Rodar com Docker Compose

```powershell
docker compose config
docker compose up -d --build
```

URLs:

- API: `http://localhost:8080`
- Swagger: `http://localhost:8080/swagger`
- Mailpit: `http://localhost:8025`

Health:

```powershell
Invoke-WebRequest http://localhost:8080/health/ready
Invoke-WebRequest http://localhost:8080/swagger/v1/swagger.json
```

Parar:

```powershell
docker compose down
```

Remover volume do PostgreSQL local:

```powershell
docker compose down -v
```

## Kubernetes local

Renderizar manifests:

```powershell
kubectl kustomize .\k8s
```

Criar namespace e secrets locais antes do deploy:

```powershell
kubectl apply -f .\k8s\namespace.yaml

kubectl -n oficina create secret generic oficina-postgres-secret `
  --from-literal=POSTGRES_USER=oficina `
  --from-literal=POSTGRES_PASSWORD="<senha-postgres>" `
  --dry-run=client -o yaml | kubectl apply -f -

kubectl -n oficina create secret generic oficina-api-secret `
  --from-literal=Jwt__Key="<chave-jwt-com-32-ou-mais-caracteres>" `
  --from-literal=AdminCredentials__Password="<senha-admin>" `
  --from-literal=Integration__Token="<token-integracao>" `
  --from-literal=ConnectionStrings__DefaultConnection="Host=oficina-postgres;Port=5432;Database=oficina;Username=oficina;Password=<senha-postgres>" `
  --dry-run=client -o yaml | kubectl apply -f -
```

Aplicar em cluster ja existente:

```powershell
kubectl apply -k .\k8s
kubectl -n oficina rollout status deploy/oficina-api --timeout=120s
kubectl -n oficina get pods,svc,hpa,pvc
```

Acessar via port-forward:

```powershell
kubectl -n oficina port-forward svc/oficina-api 8080:80
Invoke-WebRequest http://localhost:8080/health/ready
```

Observacoes:

- `k8s/secret.example.yaml` e apenas template.
- `k8s/kustomization.yaml` nao versiona Secrets; os secrets devem existir no cluster antes do deploy.
- HPA depende de metrics-server.
- Ingress e opcional; port-forward e o caminho mais simples para validacao local.

## Terraform

Arquivos em `infra/terraform` criam um cluster local kind.

```powershell
terraform -chdir=infra/terraform init
terraform -chdir=infra/terraform validate
terraform -chdir=infra/terraform plan
terraform -chdir=infra/terraform apply -auto-approve
```

Depois:

```powershell
kubectl apply -k .\k8s
```

Variaveis sensiveis devem ser passadas por ambiente (`TF_VAR_*`) ou mecanismo seguro. Nao versionar `terraform.tfvars` real.

## CI/CD

Workflows:

- `.github/workflows/build-test.yml`: restore, build, testes e cobertura critica.
- `.github/workflows/container.yml`: build da imagem Docker.
- `.github/workflows/deploy-kind.yml`: cluster kind, apply dos manifests e smoke test.

Fluxo de deploy:

1. `push` ou `pull_request` para `main` executa build, testes e cobertura critica.
2. Workflow de container faz build da imagem Docker e inspeciona o usuario configurado.
3. Workflow manual `deploy-kind` provisiona cluster kind via Terraform.
4. O pipeline cria Secrets efemeros no cluster, aplica os manifests Kubernetes e aguarda rollout.
5. O smoke test consulta `/health/ready` e `/swagger/v1/swagger.json`.

## Video de Apresentacao

Link do video: `<preencher apos upload no YouTube ou Vimeo>`

Roteiro sugerido: `docs/roteiro-video-15min-tech-challenge.md`.

## Testes

Todos os testes:

```powershell
dotnet test .\OficinaMvp.sln --configuration Release --no-build --verbosity minimal --maxcpucount:1
```

Build + testes:

```powershell
dotnet build .\OficinaMvp.sln --configuration Release --no-restore --maxcpucount:1
dotnet test .\OficinaMvp.sln --configuration Release --no-build --verbosity minimal --maxcpucount:1
```

Cobertura critica minima de 80%:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate-domain-coverage.ps1 -Minimum 80
```

Resultado validado nesta entrega:

- `OficinaMvp.Domain.Tests`: 87.82%
- `OficinaMvp.Integration.Tests`: 83.72% em entidades/excecoes de dominio

## Seguranca

Pipeline completo:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-vulnerability-report.ps1
```

Artefatos:

- `security-report/raw/metadata.json`
- `security-report/raw/dotnet-vulnerabilities.json`
- `security-report/raw/semgrep.json`
- `security-report/raw/docker-scout-cves.sarif.json`
- `security-report/relatorio-vulnerabilidades.md`
- `security-report/relatorio-vulnerabilidades.pdf`

Correcoes aplicadas:

- container da API roda como usuario non-root;
- pacote transitivo vulneravel `SQLitePCLRaw.lib.e_sqlite3 2.1.6` foi tratado com `SQLitePCLRaw.bundle_e_sqlite3 3.0.3`;
- SCA NuGet dedicado executado sem achados apos a correcao.

Resultado do scan de 2026-07-13:

- SCA NuGet: `0` achados.
- SAST Semgrep: `0` achados.
- Docker Scout: `1` achado `medium` (`CVE-2025-60876`) no `busybox` da imagem base Alpine, sem versao corrigida disponivel no momento do scan.

Observacao: para reexecutar o scan de imagem, o Docker daemon precisa estar ativo e o usuario precisa estar autenticado no Docker Desktop.

## Validacoes executadas

```powershell
dotnet build .\OficinaMvp.sln --configuration Release --no-restore --maxcpucount:1
dotnet test .\OficinaMvp.sln --configuration Release --no-build --verbosity minimal --maxcpucount:1
powershell -ExecutionPolicy Bypass -File .\scripts\validate-domain-coverage.ps1 -Minimum 80
dotnet list .\OficinaMvp.sln package --vulnerable --include-transitive --format json --source https://api.nuget.org/v3/index.json
docker compose config
docker compose up -d --build
Invoke-WebRequest http://localhost:8080/health/ready
Invoke-WebRequest http://localhost:8080/swagger/v1/swagger.json
docker compose down -v
kubectl kustomize .\k8s
terraform -chdir=infra/terraform init -backend=false
terraform -chdir=infra/terraform validate
terraform -chdir=infra/terraform plan
powershell -ExecutionPolicy Bypass -File .\scripts\run-vulnerability-report.ps1
```

Resultado validado em 2026-07-13:

- `dotnet build`: passou sem warnings.
- `dotnet test`: passou com `55` testes de dominio e `11` de integracao.
- Cobertura critica: `87.82%` nos testes de dominio e `83.72%` nos testes de integracao sobre dominio critico.
- Docker Compose: API, PostgreSQL e Mailpit subiram; `/health/ready` e Swagger responderam; stack foi derrubada no final.
- Kubernetes: `kubectl kustomize .\k8s` renderizou os manifests com sucesso.
- Terraform: `init -backend=false`, `validate` e `plan` executaram com sucesso.
- Vulnerabilidades: relatorio Markdown/PDF atualizado com SCA, Semgrep e Docker Scout.
- Paths absolutos: varredura sem ocorrencias versionadas.

## Troubleshooting

Docker daemon indisponivel:

```powershell
docker info
Get-Service com.docker.service
```

Se o servico estiver parado, abra o Docker Desktop como administrador ou inicie o servico com permissao adequada.

NuGet com proxy invalido:

```powershell
$env:HTTP_PROXY=''
$env:HTTPS_PROXY=''
$env:ALL_PROXY=''
$env:GIT_HTTP_PROXY=''
$env:GIT_HTTPS_PROXY=''
```

Processos de teste presos:

```powershell
Get-Process | Where-Object { $_.ProcessName -match 'dotnet|vstest|testhost|MSBuild' } | Stop-Process -Force
```
