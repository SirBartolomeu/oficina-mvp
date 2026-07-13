# Terraform local - Tech Challenge Fase 2

Este modulo provisiona um cluster Kubernetes local com kind para demonstrar a esteira da Fase 2.

## Pre-requisitos

- Docker Desktop ativo.
- Terraform instalado.
- `kubectl` disponivel.

No Windows, o Terraform pode ser instalado com:

```powershell
winget install Hashicorp.Terraform --accept-source-agreements --accept-package-agreements
```

Se o `PATH` nao atualizar na sessao atual, reinicie o terminal.

## Comandos

```powershell
terraform -chdir=infra/terraform init
terraform -chdir=infra/terraform validate
terraform -chdir=infra/terraform plan
terraform -chdir=infra/terraform apply -auto-approve
kubectl config use-context kind-oficina-local
kubectl apply -k .\k8s
kubectl -n oficina rollout status deploy/oficina-api --timeout=120s
```

Os manifests da aplicacao e do banco ficam em `k8s/`. Secrets reais nao devem ser versionados; use `k8s/secret.example.yaml` como referencia para ambientes fora do demo local.

## Validacao executada

Em 2026-07-13 foram executados com sucesso:

```powershell
terraform -chdir=infra/terraform init -backend=false
terraform -chdir=infra/terraform validate
terraform -chdir=infra/terraform plan
```

O `plan` indicou apenas a criacao do cluster kind local `oficina-local`.

O arquivo `.terraform.lock.hcl` deve ser versionado para fixar a versao do provider usado pelo modulo. A pasta `.terraform/` continua ignorada.
