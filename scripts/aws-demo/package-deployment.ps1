param(
    [string]$OutputPath = (Join-Path $env:TEMP 'oficina-demo-assets.tar.gz'),
    [string]$TerraformDirectory = 'infra/aws-demo'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$absoluteOutput = [System.IO.Path]::GetFullPath($OutputPath)
$directory = Split-Path -Parent $absoluteOutput
if (-not (Test-Path $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$stage = [System.IO.Path]::GetFullPath((Join-Path $tempRoot "oficina-demo-stage-$PID"))
if (-not $stage.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Diretório temporário fora do caminho esperado.' }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

Push-Location $repoRoot
try {
    $outputJson = & terraform "-chdir=$TerraformDirectory" output -json
    if ($LASTEXITCODE -ne 0) { throw 'Terraform output indisponível; execute somente após o apply autorizado.' }
    $tf = ($outputJson -join "`n") | ConvertFrom-Json
    $config = [ordered]@{
        awsRegion = $tf.aws_region.value
        rdsEndpoint = $tf.rds_endpoint.value
        rdsMasterSecretArn = $tf.rds_master_secret_arn.value
        applicationSecretArns = $tf.application_secret_arns.value
        workloadRoleArns = $tf.workload_role_arns.value
        ecrRepositoryNames = $tf.ecr_repository_names.value
        ecrRepositories = $tf.ecr_repositories.value
        queueUrls = $tf.queue_urls.value
        billingTableName = $tf.billing_table_name.value
        authTokenUrl = $tf.auth_token_url.value
    }
    foreach ($name in @('awsRegion', 'rdsEndpoint', 'rdsMasterSecretArn', 'billingTableName', 'authTokenUrl')) {
        if ([string]::IsNullOrWhiteSpace([string]$config[$name])) { throw "Output Terraform ausente: $name" }
    }
    foreach ($name in @('applicationSecretArns', 'workloadRoleArns', 'ecrRepositoryNames', 'ecrRepositories')) {
        foreach ($service in @('os', 'execution', 'billing')) {
            if ([string]::IsNullOrWhiteSpace([string]$config[$name].$service)) { throw "Output Terraform ausente: $name.$service" }
        }
    }
    foreach ($queue in @('billing_workorders', 'execution_commands', 'os_events')) {
        if ([string]::IsNullOrWhiteSpace([string]$config.queueUrls.$queue)) { throw "Output Terraform ausente: queueUrls.$queue" }
    }
    $config | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stage 'deployment-config.json') -Encoding utf8
    & tar -czf $absoluteOutput -C $repoRoot k8s/distributed k8s/monitoring scripts/aws-demo/bootstrap-rds-and-app-secrets.sh scripts/aws-demo/prepare-workload-credentials.sh scripts/aws-demo/refresh-demo-credentials.sh scripts/aws-demo/initial-deploy.sh scripts/aws-demo/render-distributed.py scripts/aws-demo/select-ecr-sha.py scripts/aws-demo/smoke-k3s.sh scripts/aws-demo/verify-flow.py scripts/aws-demo/verify-flow-k3s.sh -C $stage deployment-config.json
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao empacotar manifests e scripts.' }
    $hash = (Get-FileHash -Path $absoluteOutput -Algorithm SHA256).Hash.ToLowerInvariant()
    "Pacote: $absoluteOutput"
    "SHA256: $hash"
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $stage) {
        $verifiedStage = [System.IO.Path]::GetFullPath($stage)
        if (-not $verifiedStage.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Remoção temporária fora do caminho esperado.' }
        Remove-Item -LiteralPath $verifiedStage -Recurse -Force
    }
}
