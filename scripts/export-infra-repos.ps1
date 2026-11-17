param([string]$OutputRoot = 'exports')

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$exportRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
$allowedRoot = $repoRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $exportRoot.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Os clones locais devem ficar dentro do workspace.'
}
if (& git -C $repoRoot status --porcelain) { throw 'Registre as alterações de origem antes da extração.' }
$branch = (& git -C $repoRoot branch --show-current).Trim()
if (-not $branch) { throw 'A origem precisa estar em uma branch nomeada.' }
$sourceCommit = (& git -C $repoRoot rev-parse HEAD).Trim()

$common = @('.gitattributes', '.gitignore', 'LICENSE', 'docs/architecture-phase4.md')
$repos = [ordered]@{
    'oficina-data-infra' = @('infra/aws-data', '.github/workflows/data-infra-ci.yml')
    'oficina-k8s-infra' = @(
        'infra/aws-k3s', '.github/workflows/k3s-infra-ci.yml',
        'global.json', 'NuGet.config', 'services/Oficina.Auth', 'services/Oficina.Auth.Tests',
        'scripts/aws-demo', 'k8s/distributed', 'k8s/monitoring'
    )
}

if (-not (Test-Path -LiteralPath $exportRoot)) { New-Item -ItemType Directory -Path $exportRoot | Out-Null }
foreach ($name in $repos.Keys) {
    $target = [System.IO.Path]::GetFullPath((Join-Path $exportRoot $name))
    if (-not $target.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Destino fora do workspace.' }
    if (Test-Path -LiteralPath $target) { throw "Destino já existe: $target. Não será sobrescrito." }
    & git clone --quiet --no-hardlinks --single-branch --branch $branch -- $repoRoot $target
    if ($LASTEXITCODE -ne 0) { throw "Falha ao clonar $name." }
    & git -C $target branch -M main
    if ($LASTEXITCODE -ne 0) { throw "Falha ao criar main em $name." }
    & git -C $target remote remove origin
    if ($LASTEXITCODE -ne 0) { throw "Falha ao remover remote local em $name." }
    & git -C $target rm -r --quiet -- .
    if ($LASTEXITCODE -ne 0) { throw "Falha ao isolar arquivos de $name." }
    $keep = @($common) + @($repos[$name])
    & git -C $target restore --source=HEAD --staged --worktree -- $keep
    if ($LASTEXITCODE -ne 0) { throw "Falha ao restaurar arquivos de $name." }
    $module = if ($name -eq 'oficina-data-infra') { 'aws-data' } else { 'aws-k3s' }
    Copy-Item -LiteralPath (Join-Path $target "infra/$module/README.md") -Destination (Join-Path $target 'README.md') -Force
    Add-Content -LiteralPath (Join-Path $target 'README.md') -Value "`nExtraído do commit $sourceCommit do projeto oficina-mvp, preservando o histórico Git. Sem remote configurado."
    & git -C $target add -A
    if ($LASTEXITCODE -ne 0) { throw "Falha ao preparar commit de $name." }
    & git -C $target commit --quiet -m "Isola $name em repositorio independente"
    if ($LASTEXITCODE -ne 0) { throw "Falha ao registrar commit de $name." }
    "Preparado: $target"
}
