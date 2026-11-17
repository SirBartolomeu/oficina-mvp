param([switch]$StopAfter, [string]$EvidencePath)

$ErrorActionPreference = 'Stop'
$base = @(
    '-f', 'docker-compose.yml',
    '-f', 'compose.execution.yml',
    '-f', 'compose.billing.yml',
    '-f', 'compose.messaging.yml',
    '-f', 'compose.saga-local.yml',
    '-f', 'compose.telemetry.yml',
    '-f', 'compose.monitoring.yml'
)
$demo = $base + @('-f', 'compose.monitoring.alert-demo.yml')
$demoInstance = 'http://alert-demo:8080/health/ready'

& docker compose @base up -d oficina-api execution-api billing-api otel-collector blackbox prometheus grafana
if ($LASTEXITCODE -ne 0) { throw 'Falha ao iniciar o monitoramento local.' }

foreach ($port in @(8080, 8081, 8082)) {
    $ready = $false
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try {
            $health = Invoke-WebRequest -Uri "http://localhost:$port/health/ready" -TimeoutSec 3
            if ($health.StatusCode -eq 200) { $ready = $true; break }
        } catch { }
        Start-Sleep -Seconds 2
    }
    if (-not $ready) { throw "API na porta $port não ficou pronta." }
}

try {
    & docker compose @demo up -d --force-recreate --no-deps prometheus
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao iniciar o alvo de demonstração.' }

    $firing = $false
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        Start-Sleep -Seconds 5
        try {
            $response = Invoke-RestMethod -Uri 'http://localhost:9090/api/v1/alerts' -TimeoutSec 3
            $firing = @($response.data.alerts | Where-Object {
                $_.labels.alertname -eq 'OficinaServiceUnavailable' -and
                $_.labels.instance -eq $demoInstance -and $_.state -eq 'firing'
            }).Count -gt 0
            if ($firing) { break }
        } catch {
            # O Prometheus pode ainda estar iniciando após a recriação.
        }
    }
    if (-not $firing) { throw 'O alerta de indisponibilidade não disparou em 100 segundos.' }

    $query = [uri]::EscapeDataString('probe_success{job="oficina-health"}')
    $probes = (Invoke-RestMethod -Uri "http://localhost:9090/api/v1/query?query=$query" -TimeoutSec 5).data.result
    foreach ($instance in @(
        'http://oficina-api:8080/health/ready',
        'http://execution-api:8080/health/ready',
        'http://billing-api:8080/health/ready'
    )) {
        $probe = $probes | Where-Object { $_.metric.instance -eq $instance }
        if (@($probe).Count -ne 1 -or $probe.value[1] -ne '1') {
            throw "API indisponível durante a demonstração: $instance"
        }
    }

    if ($EvidencePath) {
        $evidence = [ordered]@{
            observedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
            alertName = 'OficinaServiceUnavailable'
            state = 'firing'
            instance = $demoInstance
            healthyApis = @($probes | Where-Object { $_.metric.instance -ne $demoInstance } |
                ForEach-Object { $_.metric.instance })
        }
        $directory = Split-Path -Parent $EvidencePath
        if ($directory) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
        $evidence | ConvertTo-Json -Depth 4 | Set-Content -Path $EvidencePath -Encoding utf8
    }

    "Alerta OficinaServiceUnavailable=firing para $demoInstance; três APIs prontas."
}
finally {
    & docker compose @base up -d --force-recreate --no-deps prometheus
    if ($LASTEXITCODE -ne 0) { Write-Warning 'Falha ao restaurar a configuração normal do Prometheus.' }
    if ($StopAfter) {
        & docker compose @base stop grafana prometheus blackbox
        & docker compose -f docker-compose.yml -f compose.execution.yml -f compose.billing.yml -f compose.telemetry.yml up -d otel-collector
    }
}
