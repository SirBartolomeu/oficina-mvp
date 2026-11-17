# Três serviços no K3s

Este Kustomize prepara OS, Execução e Billing como Deployments independentes no namespace `oficina`, cada um com Service privado, ConfigMap, ServiceAccount, healthchecks e HPA de 1–2 réplicas por CPU. Substitui o Kustomize legado da raiz `k8s/`; não aplicar os dois conjuntos sobre a mesma implantação. As imagens `:local` são marcadores até o pipeline publicar imagens no ECR.

## Dependências antes do deploy

1. Criar no RDS dois databases/usuários separados (`oficina_os` e `oficina_execution`), a tabela DynamoDB `oficina_billing` e as três filas SQS com DLQ. OS e Execução executam migrations ao iniciar em `Production`; Billing exige tabela já criada. Não há banco dentro deste overlay.
2. Substituir os `REPLACE_WITH_...` em `os.env`, `execution.env` e `billing.env` pelos URLs SQS reais, sem adicionar credenciais AWS ao Git. Ajustar região e nome da tabela de acordo com o Terraform.
3. Rodar `scripts/aws-demo/bootstrap-rds-and-app-secrets.sh` no nó K3s após o RDS ficar pronto. Ele cria os dois bancos/usuários e os Secrets `oficina-os-secret`, `oficina-execution-secret` e `oficina-billing-secret` a partir do Secrets Manager. A OS e a Execução usam a mesma credencial interna de Execução; a Lambda também precisará do token de consulta da OS quando implantada. Tokens Mercado Pago, se usados, devem ser do ambiente de testes e adicionados ao segredo do Billing fora do Git.
4. Na EC2 K3s, após criar o namespace, executar `scripts/aws-demo/prepare-workload-credentials.sh` com a região e os três ARNs do output `workload_role_arns`. O script cria um Secret AWS temporário por serviço (até 1h) e um Secret de pull ECR (até 12h). Executar pouco antes do deploy e renovar/reiniciar os pods se expirar. O SDK nos pods ignora IMDS; o nó exige IMDSv2 com hop limit 1. Para uso contínuo, trocar a renovação manual por identidade de workload automatizada.
5. Publicar as três imagens no ECR por Actions, atualizar os manifests com URIs e SHAs reais e aplicar `k8s/monitoring` na janela da demonstração para habilitar o endpoint OTLP configurado nos três serviços.

```powershell
./scripts/verify-distributed-manifests.ps1
kubectl apply -k k8s/distributed
kubectl -n oficina rollout status deployment/oficina-api --timeout=300s
kubectl -n oficina rollout status deployment/execution-api --timeout=300s
kubectl -n oficina rollout status deployment/billing-api --timeout=300s
kubectl -n oficina get hpa
```

O HPA depende do metrics-server do K3s e não cria nós: se faltar capacidade na EC2, uma segunda réplica ficará pendente. OS e Execução têm startup probe de até cinco minutos para migrations; a readiness consulta seus bancos. Billing consulta a tabela DynamoDB. As três APIs reservam juntas 300 millicores/384 MiB e têm limites somados de 1500 millicores/1536 MiB por réplica inicial. A telemetria usa o Collector no Service `otel-collector:4317`.

`auth-lookup-nodeport.yaml` abre a consulta interna da OS somente na porta 30080 do nó; o security group AWS aceita essa porta apenas da Lambda de autenticação. O token interno continua obrigatório na rota. `ingress.example.yaml` é referência para publicar `/api` da OS e o webhook do Mercado Pago; não expõe `/internal` publicamente. Exige host real e certificado TLS antes de aplicar. Nenhum `kubectl apply` foi executado nesta etapa: faltam AWS e Secrets. [Referência IMDS da AWS](https://docs.aws.amazon.com/sdkref/latest/guide/feature-imds-credentials.html).
