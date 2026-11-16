param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$compose = @(
    '-f', 'docker-compose.yml',
    '-f', 'compose.execution.yml',
    '-f', 'compose.billing.yml',
    '-f', 'compose.messaging.yml',
    '-f', 'compose.saga-local.yml',
    '-f', 'compose.telemetry.yml',
    '-f', 'compose.telemetry.test.yml'
)

& docker compose @compose up -d oficina-db execution-db dynamodb-local elasticmq
if ($LASTEXITCODE -ne 0) { throw 'Falha ao iniciar bancos ou ElasticMQ.' }

foreach ($database in @(
    @{ Container = 'oficina-mvp-db'; User = 'oficina'; Name = 'oficina_saga_migrated_local' },
    @{ Container = 'oficina-mvp-execution-db-1'; User = 'execution'; Name = 'oficina_execution_saga_local' }
)) {
    $exists = & docker exec $database.Container psql -U $database.User -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$($database.Name)'"
    if ($LASTEXITCODE -ne 0) { throw "Falha ao consultar $($database.Name)." }
    if (-not $exists) {
        & docker exec $database.Container createdb -U $database.User $database.Name
        if ($LASTEXITCODE -ne 0) { throw "Falha ao criar $($database.Name)." }
    }
}

foreach ($queue in @('billing-workorders', 'os-events', 'execution-commands')) {
    $result = & curl.exe -fsS -X POST -d "Action=CreateQueue&QueueName=$queue&Version=2012-11-05" 'http://localhost:9324/'
    if ($LASTEXITCODE -ne 0 -or ($result -join '') -notmatch 'QueueUrl') { throw "Falha ao criar a fila $queue." }
}

$build = if ($SkipBuild) { @() } else { @('--build') }
& docker compose @compose up -d @build oficina-api execution-api billing-api otel-collector
if ($LASTEXITCODE -ne 0) { throw 'Falha ao iniciar as APIs e o Collector.' }

foreach ($port in @(8080, 8081, 8082)) {
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            $status = & curl.exe -s -o NUL -w '%{http_code}' "http://localhost:$port/health/ready"
            if ($status -eq '200') { $ready = $true; break }
        } catch { }
        Start-Sleep -Seconds 2
    }
    if (-not $ready) { throw "API na porta $port não ficou pronta." }
}

$token = (Invoke-RestMethod -Method Post -Uri 'http://localhost:8080/api/auth/token' -ContentType 'application/json' -Body '{"username":"admin","password":"Admin@123"}').accessToken
$name = 'Trace local ' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$serviceBody = @{ name = $name; description = 'Validacao distribuida'; laborPrice = 125;
    averageDurationMinutes = 30 } | ConvertTo-Json
$service = Invoke-RestMethod -Method Post -Uri 'http://localhost:8081/internal/catalog/services' -Headers @{ 'X-Internal-Token' = 'execution-local-internal-token-only-123456' } -ContentType 'application/json' -Body $serviceBody
$orderBody = @{
    customerDocument = '52998224725'
    vehicle = @{ licensePlate = 'BRA2E19'; brand = 'Volkswagen'; model = 'Gol'; year = 2021 }
    services = @(@{ serviceId = $service.id; quantity = 1 })
    parts = @()
    branchId = 'sp-centro'
} | ConvertTo-Json -Depth 8
$response = Invoke-WebRequest -Method Post -Uri 'http://localhost:8080/api/work-orders' -Headers @{ Authorization = "Bearer $token" } -ContentType 'application/json' -Body $orderBody
$order = $response.Content | ConvertFrom-Json
$trace = [string]$response.Headers['X-Trace-Id']
if ($trace -notmatch '^[0-9a-f]{32}$') { throw 'X-Trace-Id inválido.' }

$approved = $false
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    $current = Invoke-RestMethod -Uri "http://localhost:8080/api/work-orders/$($order.id)" -Headers @{ Authorization = "Bearer $token" }
    if ($current.processState -eq 'AwaitingApproval') { $approved = $true; break }
    Start-Sleep -Seconds 2
}
if (-not $approved) { throw "A OS $($order.id) não chegou a AwaitingApproval." }

$expected = @('oficina-os', 'oficina-execution', 'oficina-billing')
$services = New-Object 'System.Collections.Generic.HashSet[string]'
for ($attempt = 0; $attempt -lt 15; $attempt++) {
    $currentService = ''
    foreach ($line in (& docker logs oficina-mvp-otel-collector-1 --since 2m 2>&1)) {
        if ($line -match '-> service.name: Str\(([^)]+)\)') { $currentService = $Matches[1] }
        elseif ($line -match "Trace ID\s+:\s+$trace") { [void]$services.Add($currentService) }
    }
    if (@($expected | Where-Object { -not $services.Contains($_) }).Count -eq 0) { break }
    Start-Sleep -Seconds 2
}
foreach ($name in $expected) {
    if (-not $services.Contains($name)) { throw "Trace $trace ausente em $name no Collector." }
}
"OS=$($order.id) status=$($current.processState) total=$($current.budgetTotal) trace=$trace services=$($expected -join ',')"
