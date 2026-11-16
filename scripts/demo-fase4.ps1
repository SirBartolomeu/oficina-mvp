param(
    [switch]$SkipBuild,
    [switch]$IncludeAlert,
    [string]$EvidencePath = 'docs/evidence/demo-local.json'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$previousSqsEndpoint = $env:MESSAGING_SQS_ENDPOINT
$previousDynamoEndpoint = $env:BILLING_DYNAMO_ENDPOINT
Push-Location $repoRoot
try {
    $flow = & ./scripts/verify-saga-containers.ps1 -SkipBuild:$SkipBuild 2>&1
    $flowLine = $flow | ForEach-Object { $_.ToString() } |
        Where-Object { $_ -match '^OS=' } | Select-Object -Last 1
    if (-not $flowLine -or $flowLine -notmatch '^OS=([0-9a-f-]{36}).*trace=([0-9a-f]{32})') {
        throw 'O roteiro da Saga não retornou OS e trace válidos.'
    }
    $orderId = $Matches[1]
    $traceId = $Matches[2]
    $flowLine

    $compose = @(
        '-f', 'docker-compose.yml',
        '-f', 'compose.execution.yml',
        '-f', 'compose.billing.yml',
        '-f', 'compose.messaging.yml',
        '-f', 'compose.saga-local.yml',
        '-f', 'compose.telemetry.yml',
        '-f', 'compose.gateway.yml',
        '-f', 'compose.monitoring.yml'
    )
    $build = if ($SkipBuild) { @() } else { @('--build') }
    & docker compose @compose up -d @build auth-local gateway otel-collector prometheus blackbox grafana
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao iniciar gateway e dashboard.' }

    $clientToken = $null
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try {
            $auth = Invoke-RestMethod -SkipCertificateCheck -Method Post -Uri 'https://localhost:8443/auth/token' -ContentType 'application/json' -Body '{"cpf":"52998224725"}' -TimeoutSec 5
            $clientToken = $auth.accessToken
            if ($clientToken) { break }
        } catch { Start-Sleep -Seconds 2 }
    }
    if (-not $clientToken) { throw 'A autenticação do cliente via gateway não ficou pronta.' }

    $tracking = Invoke-RestMethod -SkipCertificateCheck -Uri "https://localhost:8443/api/client/work-orders/$orderId" -Headers @{ Authorization = "Bearer $clientToken" } -TimeoutSec 10
    if ($tracking.workOrderId -ne $orderId) { throw 'O tracking não retornou a OS criada.' }
    'CPF -> JWT -> tracking protegido: OK'

    $env:MESSAGING_SQS_ENDPOINT = 'http://localhost:9324'
    $env:BILLING_DYNAMO_ENDPOINT = 'http://localhost:8000'
    $dotnet = Join-Path $repoRoot '.dotnet_home/sdk10/dotnet.exe'
    & $dotnet test tests/Oficina.Messaging.Local.Tests/Oficina.Messaging.Local.Tests.csproj --no-restore --nologo -v quiet --filter 'DisplayName~Abertura percorre'
    if ($LASTEXITCODE -ne 0) { throw 'O cenário Gherkin distribuído da Saga falhou.' }
    'BDD Gherkin: Saga até execução com pagamento simulado: OK'

    $dashboardReady = $false
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try {
            $dashboard = Invoke-RestMethod -Uri 'http://localhost:3000/api/dashboards/uid/oficina-local' -TimeoutSec 5
            if ($dashboard.dashboard.panels.Count -eq 9) {
                $dashboardReady = $true
                break
            }
        } catch { Start-Sleep -Seconds 2 }
    }
    if (-not $dashboardReady) { throw 'Dashboard Grafana não ficou pronto com nove painéis.' }
    'Dashboard Grafana com nove painéis: OK'

    if ($IncludeAlert) {
        & ./scripts/verify-alert-local.ps1
        if ($LASTEXITCODE -ne 0) { throw 'Demonstração do alerta falhou.' }
    }

    $evidence = [ordered]@{
        observedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        orderId = $orderId
        traceId = $traceId
        orderState = 'AwaitingApproval'
        customerTracking = 'passed'
        sagaTest = 'passed_with_simulated_payment_provider'
        bddScenario = 'Abertura percorre aprovação, reserva, pagamento e execução'
        dashboardPanels = 9
        alertDemonstrated = [bool]$IncludeAlert
    }
    $directory = Split-Path -Parent $EvidencePath
    if ($directory) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    $evidence | ConvertTo-Json -Depth 4 | Set-Content -Path $EvidencePath -Encoding utf8

    'Gateway: https://localhost:8443'
    'Grafana: http://localhost:3000/d/oficina-local'
    'Prometheus: http://localhost:9090/alerts'
    "Evidência: $EvidencePath"
}
finally {
    $env:MESSAGING_SQS_ENDPOINT = $previousSqsEndpoint
    $env:BILLING_DYNAMO_ENDPOINT = $previousDynamoEndpoint
    Pop-Location
}
