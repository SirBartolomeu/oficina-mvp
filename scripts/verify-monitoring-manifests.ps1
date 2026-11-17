$ErrorActionPreference = 'Stop'

$files = @(
    @('config/otel-collector-prometheus.yaml', 'k8s/monitoring/collector-config.yaml'),
    @('config/prometheus-alerts.yaml', 'k8s/monitoring/alerts.yaml'),
    @('config/blackbox-local.yaml', 'k8s/monitoring/blackbox-config.yaml'),
    @('config/grafana/datasources.yaml', 'k8s/monitoring/datasources.yaml'),
    @('config/grafana/dashboards.yaml', 'k8s/monitoring/dashboards.yaml'),
    @('config/grafana/oficina.json', 'k8s/monitoring/oficina.json')
)

foreach ($pair in $files) {
    $source = (Get-FileHash $pair[0]).Hash
    $copy = (Get-FileHash $pair[1]).Hash
    if ($source -ne $copy) { throw "Configuração divergente: $($pair[0]) e $($pair[1])." }
}

$manifest = & kubectl kustomize k8s/monitoring
if ($LASTEXITCODE -ne 0) { throw 'Falha ao renderizar os manifests de monitoramento.' }
$resources = @($manifest | Where-Object { $_ -match '^kind: (ConfigMap|Deployment|Service)$' }).Count
if ($resources -ne 13) { throw "Esperados 13 recursos; encontrados $resources." }

"Kustomize: $resources recursos; seis configurações compartilhadas idênticas."
