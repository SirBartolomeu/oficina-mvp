# Oficina MVP - Tech Challenge Fase 1

API REST para gestao de oficina mecanica, implementada em `.NET 8` com `C#`, arquitetura em camadas inspirada em DDD, banco `SQLite`, autenticacao JWT, testes automatizados e execucao via Docker Compose.

## Documentacao DDD do desafio

- Documento principal: `docs/documentacao-ddd-tech-challenge.md`
- Diagramas visuais:
  - `docs/diagramas/domain-storytelling-oficina-mvp.svg`
  - `docs/diagramas/event-storming-oficina-mvp.svg`

## Objetivo do projeto

Entregar um MVP com:
- cadastro de clientes, veiculos, servicos e pecas
- abertura e ciclo de vida de ordens de servico
- controle de estoque de pecas
- autenticacao para endpoints administrativos
- acompanhamento de status da OS para o cliente

## Arquitetura da solucao

Monolito modular em camadas:

- `Domain`
  - Entidades principais: `WorkOrder`, `Customer`, `Vehicle`, `PartSupply`, `RepairService`, `WorkOrderStatusHistory`
  - Regras de negocio e validacoes de dominio (`DocumentValidator`, `LicensePlateValidator`)
- `Application`
  - Orquestracao de casos de uso da OS via `WorkOrderApplicationService`
  - Contratos de entrada e saida (DTOs) em `Application/Contracts`
- `Infrastructure`
  - Persistencia com EF Core e `WorkshopDbContext`
  - Seguranca com JWT (`TokenService`, `JwtOptions`, `AdminCredentialsOptions`)
- `Presentation (API)`
  - Controllers REST (`WorkOrdersController`, `CustomersController`, `VehiclesController`, `PartsController`, `ServicesController`, `AuthController`, `ClientTrackingController`)
  - Middleware global de excecoes (`ExceptionHandlingMiddleware`)
  - Swagger/OpenAPI para exploracao da API

## Estrutura de pastas

```text
oficina-mvp/
  src/OficinaMvp.Api/                  # API + dominio + aplicacao + infraestrutura
  tests/OficinaMvp.Domain.Tests/       # testes unitarios de dominio
  tests/OficinaMvp.Integration.Tests/  # testes de integracao da API
  docker-compose.yml
  README.md
```

## Tecnologias e bibliotecas

- Plataforma:
  - `.NET 8`
  - `ASP.NET Core Web API`
- Persistencia:
  - `Entity Framework Core 8`
  - `Microsoft.EntityFrameworkCore.Sqlite`
- Seguranca:
  - `Microsoft.AspNetCore.Authentication.JwtBearer`
- Documentacao de API:
  - `Swashbuckle.AspNetCore` (Swagger)
- Testes:
  - `xUnit`
  - `Microsoft.NET.Test.Sdk`
  - `Microsoft.AspNetCore.Mvc.Testing`
  - `coverlet.collector`

## Banco de dados: por que SQLite

`SQLite` foi escolhido para o MVP porque:
- reduz custo de operacao e setup (arquivo local, sem servidor dedicado)
- acelera desenvolvimento e validacao funcional
- integra de forma nativa com EF Core
- facilita execucao local e em container

No projeto atual:
- ambiente local/compose usa arquivo SQLite (`Data Source=oficina.db` ou `/data/oficina.db`)
- testes de integracao usam SQLite em memoria (`Data Source=:memory:`) para isolamento e velocidade
- nao ha pasta de migrations; o schema e criado com `EnsureCreated()`
- existe seed idempotente de dados de demonstracao para ambiente `Development`

## Seed de demo

A aplicacao inclui seed de dados para facilitar validacao em qualquer maquina:
- cliente demo
- veiculo demo
- servicos demo
- pecas/insumos demo

Comportamento padrao:
- roda automaticamente em `Development`
- nao roda em `Testing`

Para desativar a seed em `Development`:

```powershell
$env:SeedDemoData = "false"
dotnet run --project .\src\OficinaMvp.Api --launch-profile http
```

## Pre-requisitos

- `.NET SDK` compativel com `net8.0`
- Docker Desktop (para execucao com compose)

Instalacao do Docker Desktop (Windows):

```powershell
winget install -e --id Docker.DockerDesktop --accept-package-agreements --accept-source-agreements
```

Se `docker` nao for reconhecido apos instalar:
- abra nova sessao de terminal
- valide `docker --version`
- se necessario, ajuste o `PATH` do Docker Desktop e abra nova sessao

## Como rodar localmente (sem Docker)

Na raiz `oficina-mvp`:

```powershell
dotnet restore .\OficinaMvp.sln
dotnet run --project .\src\OficinaMvp.Api --launch-profile http
```

Endpoints locais:
- API: `http://localhost:5245`
- Swagger: `http://localhost:5245/swagger`

## Autenticacao

Credenciais administrativas padrao:
- usuario: `admin`
- senha: `Admin@123`

Gerar token:
- `POST /api/auth/token`

Uso no Swagger:
- `Bearer {token}`

## Como rodar no Docker Compose

Na raiz `oficina-mvp`:

```powershell
docker compose up -d --build
```

Endpoints em container:
- API: `http://localhost:8080`
- Swagger: `http://localhost:8080/swagger`

Parar containers:

```powershell
docker compose down
```

## Testes automatizados

Execucao unica de todos os testes da solucao:

```powershell
dotnet test .\OficinaMvp.sln --verbosity minimal -m:1
```

Cobertura (opcional):

```powershell
dotnet test .\OficinaMvp.sln --collect:"XPlat Code Coverage" -m:1
```

Validacao de cobertura minima (80%) nos dominios criticos:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate-domain-coverage.ps1 -Minimum 80
```

## Relatorio de vulnerabilidades

Pipeline completo (SCA + SAST + imagem):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-vulnerability-report.ps1
```

Saidas geradas:
- `security-report/raw/metadata.json`
- `security-report/raw/dotnet-vulnerabilities.json`
- `security-report/raw/semgrep.json`
- `security-report/raw/docker-scout-cves.sarif.json`
- `security-report/relatorio-vulnerabilidades.md`
- `security-report/relatorio-vulnerabilidades.pdf`

Observacao:
- o pipeline falha de forma explicita se qualquer etapa obrigatoria falhar (incluindo scan de imagem)
- para o scan de imagem funcionar, o Docker daemon precisa estar acessivel (`docker info` sem erro)

## Troubleshooting rapido

Erro de permissao no Docker (`permission denied ... dockerDesktopLinuxEngine`):
- garantir usuario no grupo local `docker-users`
- fazer logoff/login apos ajustar grupo
- validar:

```powershell
docker info
docker compose version
```

Se houver muitos processos `dotnet` apos cancelamentos:

```powershell
Get-Process | Where-Object { $_.ProcessName -match 'dotnet|vstest|testhost|MSBuild' } | Stop-Process -Force
```
