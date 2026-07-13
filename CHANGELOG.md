# Changelog

Registro das principais entregas do projeto Oficina MVP nas fases do Tech Challenge.

## Fase 1

- API REST em .NET 8 para gestao de oficina mecanica.
- DDD tatico com agregado principal `WorkOrder` concentrando regras de negocio.
- CRUD de clientes, veiculos, servicos e pecas/insumos.
- Fluxo completo de Ordem de Servico: recebida, diagnostico, orcamento, aprovacao, execucao, finalizacao e entrega.
- Controle de estoque de pecas consumidas na OS.
- JWT protegendo rotas administrativas.
- Tracking publico de OS por identificador e documento do cliente.
- Persistencia local com SQLite.
- Docker Compose inicial para execucao containerizada.
- Testes unitarios de dominio e testes de integracao para fluxos criticos.
- Documentacao DDD com Domain Storytelling, Event Storming e glossario PT -> EN.
- Relatorio de vulnerabilidades com SCA, SAST e scan de imagem.

## Fase 2

- Refatoracao para Clean Architecture explicita.
- Separacao da solucao em `OficinaMvp.Domain`, `OficinaMvp.Application`, `OficinaMvp.Infrastructure` e `OficinaMvp.Api`.
- Suporte configuravel a SQLite para local/testes e PostgreSQL para Docker/Kubernetes.
- Endpoint explicito `GET /api/work-orders/{id}/status`.
- Endpoint externo `POST /api/integrations/work-orders/{id}/budget-decision` protegido por `X-Integration-Token`.
- Listagem de OS ativas com prioridade por status e ordenacao por antiguidade.
- Exclusao logica de OS finalizadas e entregues da listagem administrativa.
- Notificacao de status por porta `IWorkOrderStatusNotifier`, com implementacoes log e SMTP.
- Health checks `/health/live` e `/health/ready`.
- Dockerfile atualizado para executar a API como usuario non-root.
- Docker Compose revisado com API, PostgreSQL e Mailpit.
- Kubernetes com namespace, Deployment, Service, ConfigMap, Secret template, HPA, probes, PVC e StatefulSet PostgreSQL.
- Terraform para provisionar cluster local kind.
- GitHub Actions para build/test/cobertura, Docker build e deploy em kind.
- Correcao da vulnerabilidade transitiva do SQLite via `SQLitePCLRaw.bundle_e_sqlite3 3.0.3`.
- Reexecucao completa do relatorio de vulnerabilidades com SCA NuGet, SAST Semgrep e Docker Scout.
- Validacao local de Docker Compose com API, PostgreSQL, Mailpit, health check e Swagger.
- Validacao de Terraform com `init -backend=false`, `validate` e `plan`.
- Atualizacao da documentacao DDD, Event Storming Fase 2, diagrama de agregados e arquitetura Kubernetes.
- Roteiro de video atualizado para demonstrar deploy, CI/CD, APIs, escalabilidade e evidencias tecnicas da Fase 2.
