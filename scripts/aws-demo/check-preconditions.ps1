param(
    [string]$MetadataPath = 'docs/submission-metadata.json'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$checks = [System.Collections.Generic.List[object]]::new()

function Add-Check([string]$item, [bool]$ready, [string]$next) {
    $checks.Add([pscustomobject]@{
        Item = $item
        Estado = if ($ready) { 'OK' } else { 'PENDENTE' }
        ProximoPasso = if ($ready) { '' } else { $next }
    })
}

function Find-Tool([string]$name, [string]$fallback) {
    $command = Get-Command $name -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    if ($fallback -and (Test-Path -LiteralPath $fallback)) { return $fallback }
    return $null
}

$aws = Find-Tool 'aws' 'C:\Program Files\Amazon\AWSCLIV2\aws.exe'
Add-Check 'AWS CLI' ([bool]$aws) 'Instalar AWS CLI v2 e abrir novo PowerShell.'
if ($aws) {
    $profiles = @(& $aws configure list-profiles 2>$null)
    Add-Check 'Perfil AWS' ($LASTEXITCODE -eq 0 -and $profiles.Count -gt 0) 'Configurar SSO/perfil temporario; confirmar com sts get-caller-identity.'
}

$gh = Find-Tool 'gh' ''
if (-not $gh) {
    $packageRoot = Join-Path $env:USERPROFILE 'AppData\Local\Microsoft\WinGet\Packages'
    if (Test-Path -LiteralPath $packageRoot) {
        $gh = Get-ChildItem -LiteralPath $packageRoot -Directory -Filter 'GitHub.cli_*' |
            ForEach-Object { Join-Path $_.FullName 'bin\gh.exe' } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
    }
}
Add-Check 'GitHub CLI' ([bool]$gh) 'Instalar GitHub CLI e abrir novo PowerShell.'
if ($gh) {
    & $gh auth status *> $null
    Add-Check 'Login GitHub' ($LASTEXITCODE -eq 0) 'Executar gh auth login --web --hostname github.com --git-protocol https.'
}

$repositories = @('oficina-os', 'oficina-execution', 'oficina-billing',
    'oficina-auth-lambda', 'oficina-data-infra', 'oficina-k8s-infra')
foreach ($name in $repositories) {
    $directory = Join-Path $repoRoot "exports/$name"
    $exists = Test-Path -LiteralPath (Join-Path $directory '.git')
    Add-Check "Clone $name" $exists 'Recriar o clone local pelo exportador correspondente.'
    if ($exists) {
        $branch = (& git -C $directory branch --show-current).Trim()
        $remoteUrl = (& git -C $directory remote get-url origin 2>$null)
        $dirty = @(& git -C $directory status --porcelain)
        Add-Check "main $name" ($branch -eq 'main') 'Conferir a branch principal do clone.'
        Add-Check "limpo $name" ($dirty.Count -eq 0) 'Registrar alteracoes locais antes de publicar.'
        Add-Check "remote $name" ([string]$remoteUrl -match '^(https://github\.com/|git@github\.com:)') 'Publicar somente apos autorizacao explicita; depois configurar origin GitHub.'
    }
}

$metadataFile = Join-Path $repoRoot $MetadataPath
Add-Check 'Metadados PDF' (Test-Path -LiteralPath $metadataFile) 'Copiar docs/submission-metadata.example.json e preencher dados reais.'
if (Test-Path -LiteralPath $metadataFile) {
    $metadata = Get-Content -LiteralPath $metadataFile -Raw | ConvertFrom-Json
    $participants = @($metadata.participants)
    Add-Check 'Identificacao PDF' ($participants.Count -gt 0 -and
        @($participants | Where-Object { -not $_.name -or -not $_.rm }).Count -eq 0) 'Preencher nome e RM dos participantes.'
    $repositoryUrls = @('os', 'execution', 'billing', 'auth', 'k8s_infra', 'data_infra') |
        ForEach-Object { [string]$metadata.repositories.$_ }
    Add-Check 'URLs dos seis repos' (@($repositoryUrls | Where-Object { $_ -match '^https://' }).Count -eq 6) 'Substituir URL REPO 1 a 6 pelos links reais.'
    Add-Check 'URL do video' ([string]$metadata.video_url -match '^https://') 'Gravar video <=15 min e preencher URL VIDEO.'
    Add-Check 'URL da documentacao' ([string]$metadata.documentation_url -match '^https://') 'Publicar documentacao e preencher URL DOCUMENTACAO.'
    Add-Check 'Colaborador soat-architecture' ($metadata.soat_architecture_added -eq $true) 'Confirmar acesso aos seis repos e marcar nos metadados.'
}

$checks | Format-Table -AutoSize | Out-String -Width 180 | Write-Output
$pending = @($checks | Where-Object Estado -eq 'PENDENTE').Count
"Precondicoes pendentes: $pending/$($checks.Count). Nenhum recurso remoto foi criado ou alterado."
"A checagem nao valida conta AWS, budget, permissoes IAM, Terraform plan ou runs GitHub."
if ($pending) { exit 1 }
