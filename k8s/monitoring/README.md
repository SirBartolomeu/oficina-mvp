# Monitoramento opcional para a demonstração K3s

Este overlay instala Collector OTLP, Prometheus, Blackbox e Grafana no namespace `oficina`. Todos os Services são `ClusterIP`; não há Ingress nem exposição pública das interfaces. A configuração usa as mesmas regras e dashboard do Compose local. Execute depois de instalar os três serviços da aplicação com nomes `oficina-api`, `execution-api` e `billing-api`, todos com `/health/ready` na porta 80 do Service.

```powershell
./scripts/verify-monitoring-manifests.ps1
kubectl apply -k k8s/monitoring
kubectl -n oficina rollout status deployment/otel-collector --timeout=120s
kubectl -n oficina rollout status deployment/prometheus --timeout=120s
kubectl -n oficina rollout status deployment/grafana --timeout=120s
kubectl -n oficina port-forward service/grafana 3000:3000
```

No navegador local, abrir `http://localhost:3000/d/oficina-local`. Para verificar os alertas, usar `kubectl -n oficina port-forward service/prometheus 9090:9090` e abrir `http://localhost:9090/alerts`. `k8s/distributed` já configura `Observability__OtlpEndpoint=http://otel-collector:4317` nos três serviços. `kubectl -n oficina top pods` usa o metrics-server do K3s para recursos de pod e HPA; os gráficos Grafana de CPU/memória atuais são dos processos .NET.

Os quatro Deployments pedem, juntos, 135 millicores e 320 MiB de memória; os limites somam 850 millicores e 1216 MiB. Prometheus guarda até duas horas/128 MB de amostras em `emptyDir` de 256 MiB, e Grafana usa outro `emptyDir` de 256 MiB. Perder o pod perde os dados. A retenção por tamanho não é um limite rígido para WAL/cabeçalho; observar espaço livre durante a apresentação. Não manter a pilha ligada fora da janela de demonstração. Para removê-la, executar `kubectl delete -k k8s/monitoring` apenas no cluster de demonstração quando autorizado.

As configurações copiadas em `k8s/monitoring` são conferidas por `scripts/verify-monitoring-manifests.ps1`; atualizar a cópia ao alterar a origem em `config/`. O dashboard é anônimo com papel Viewer somente dentro do cluster ou via `port-forward`. A entrega de notificações ainda não está configurada. Não houve `kubectl apply` porque o cluster K3s final e os Services de Execução/Billing ainda não foram provisionados.
