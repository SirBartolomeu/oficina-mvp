# Pipelines e saúde

## Seis repositórios

| Repositório | Execução principal | Resultado | SHA |
|---|---|---|---|
| `oficina-data-infra` | [#12](https://github.com/SirBartolomeu/oficina-data-infra/actions/runs/44145922631) | sucesso | `c86c374be3c66070b724075f8ad8eff6ef33d704` |
| `oficina-k8s-infra` | [#17](https://github.com/SirBartolomeu/oficina-k8s-infra/actions/runs/44164223478) | sucesso | `3bcf64bb7e9974fa360e26d00466d5fe669d42e8` |
| `oficina-auth-lambda` | [#6](https://github.com/SirBartolomeu/oficina-auth-lambda/actions/runs/44178681060) | sucesso; Lambda publicada | `ecb86b8e44aa79406541b65eeee512ceb0e2b735` |
| `oficina-os` | [#6](https://github.com/SirBartolomeu/oficina-os/actions/runs/44175724823) | sucesso | `c61655db1e19976c39506ac969baa0f9c1c0d24c` |
| `oficina-execution` | [#6](https://github.com/SirBartolomeu/oficina-execution/actions/runs/44175711626) | sucesso | `a8e2e1bd8c9daec81aeec80b09845d9e83dea881` |
| `oficina-billing` | [#7](https://github.com/SirBartolomeu/oficina-billing/actions/runs/44181683756) | sucesso; testes, cobertura, imagem ECR e deploy | `78d83f68f49a14a61ac1a803c2eee1e64b4c8382` |

Correções do pacote Auth foram integradas nos [PR #1](https://github.com/SirBartolomeu/oficina-auth-lambda/pull/1) (runtimeconfig) e [PR #2](https://github.com/SirBartolomeu/oficina-auth-lambda/pull/2) (`Amazon.Lambda.Core`). Os respectivos CI passaram; o CI Auth reportou 13/13 testes.

## Cobertura dos pipelines

- OS: `1865/2282` linhas, **81,73%** (mínimo 80%).
- Execution: `711/841` linhas, **84,54%** (mínimo 80%).
- Billing: `1340/1563` linhas, **85,73%** (mínimo 80%).
- Auth: 13 testes aprovados no CI.

## K3s

Verificação por SSM `4f7befb4-d806-489a-b8d8-158b47b13d42`:

```text
oficina-api /health/ready: HTTP 200
execution-api /health/ready: HTTP 200
billing-api /health/ready: HTTP 200
```

Deployments OS, Execution, Billing, Grafana, Prometheus, blackbox, OTel Collector e Tempo estavam `1/1`; pods correspondentes `Running`, sem restarts reportados. O único node estava `Ready`. SSM manteve a instância `Online`.
