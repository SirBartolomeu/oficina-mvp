$ErrorActionPreference = 'Stop'

$rendered = & kubectl kustomize k8s/distributed
if ($LASTEXITCODE -ne 0) { throw 'Falha ao renderizar k8s/distributed.' }

$expected = @{
    Namespace = 1
    ServiceAccount = 3
    ConfigMap = 3
    Deployment = 3
    Service = 4
    HorizontalPodAutoscaler = 3
}
foreach ($kind in $expected.Keys) {
    $found = @($rendered | Where-Object { $_ -eq "kind: $kind" }).Count
    if ($found -ne $expected[$kind]) {
        throw "Recursos ${kind}: esperados $($expected[$kind]), encontrados $found."
    }
}

foreach ($service in @('oficina-api', 'execution-api', 'billing-api')) {
    if (-not ($rendered | Where-Object { $_ -eq "  name: $service" })) {
        throw "Service ausente: $service."
    }
}
if ($rendered -match 'REPLACE_RDS_HOST|REPLACE_MERCADO_PAGO_TEST_ACCESS_TOKEN') {
    throw 'Secret de exemplo não deve ser incluído no Kustomize.'
}

'Kustomize: 17 recursos (3 Deployments, 4 Services, 3 ConfigMaps, 3 ServiceAccounts e 3 HPAs).'
