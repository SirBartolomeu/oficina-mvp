# Demonstração Fase 4 — 15 minutos

## Preparação

Com Docker Desktop aberto, na raiz do repositório:

```powershell
./scripts/demo-fase4.ps1 -SkipBuild
```

Na primeira execução ou após mudanças nas imagens, omitir `-SkipBuild`. O comando cria uma OS, verifica o fluxo entre OS, Execução e Billing, autentica um cliente pelo adaptador local da Lambda, consulta o tracking protegido, roda o cenário Gherkin distribuído de abertura até execução e confirma o dashboard. A saída registra os identificadores e URLs; o resumo fica em `docs/evidence/demo-local.json`. Não exibir o token JWT durante a apresentação.

Se a AWS estiver pronta, execute `bash /opt/oficina-demo/scripts/aws-demo/refresh-demo-credentials.sh` no nó K3s pouco antes de iniciar. Deixe abertos os runs reais de OS, Execução, Billing e Auth e as telas AWS; não gaste a janela de 15 minutos esperando provisionamento.

## Roteiro cronometrado, se AWS estiver implantada

| Tempo | Mostrar | Mensagem principal |
|---|---|---|
| 0–2 min | [Arquitetura](architecture-phase4.md) | Três serviços .NET, bancos próprios SQL/NoSQL, Saga por filas e autenticação serverless. |
| 2–5 min | GitHub Actions: três pipelines CI/CD de serviços e run Auth | Testes unitários e PostgreSQL/DynamoDB, gates locais de 80% nos três serviços, imagens, OIDC, publicação ECR, rollout Kubernetes e atualização da Lambda **somente se os runs reais tiverem passado**. |
| 5–8 min | Console AWS e `kubectl get pods,svc,hpa` | EC2/K3s, RDS, DynamoDB, SQS/DLQ e três pods prontos; mostrar SHA de imagem e healthchecks. |
| 8–12 min | `bash /opt/oficina-demo/scripts/aws-demo/verify-flow-k3s.sh` e demo local | Mostrar OS/trace AWS até `AwaitingApproval` e CPF → JWT pela Lambda; o tracking da AWS usa port-forward no nó. No Docker, demonstrar Saga com pagamento simulado e uma compensação. |
| 12–14 min | Grafana/traces e alerta | Métricas de negócio, correlação distribuída e falha observável. |
| 14–15 min | [Matriz de evidências](evidence/phase4-status.md) | Custos da janela curta, requisitos comprovados e pendências honestas. |

## Contingência e limites

Se AWS não estiver implantada, usar a última execução registrada em `docs/evidence/demo-local.json` e o demo Docker para funcionalidade. Mostrar `deploy-kind.yml` e `phase4-local.yml` como runs do GitHub somente se o repositório original também tiver sido publicado com autorização e os workflows realmente executados. O arquivo de evidência registra verificações, mas não substitui a demonstração ao vivo. Se o script completo demorar, destacar os containers já ativos e o teste focado.

O teste de Saga usa provedor de pagamento simulado. No estado atual, K3s foi verificado estaticamente, mas não houve implantação AWS, transação real de testes no Mercado Pago nem publicação dos repositórios separados. Os gates de cobertura passaram **localmente**; ainda faltam runs remotos como evidência acadêmica. Só atualizar as afirmações de deploy e integração externa após evidência remota. O passo a passo manual está em [preparação GitHub/AWS](runbooks/preparacao-github-aws.md).
