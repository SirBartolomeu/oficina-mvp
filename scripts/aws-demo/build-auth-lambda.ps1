param(
    [string]$OutputPath = 'infra/aws-demo/artifacts/oficina-auth.zip',
    [string]$DotnetPath = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$zipPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputPath))
$zipDir = Split-Path -Parent $zipPath
if (-not (Test-Path -LiteralPath $zipDir)) { New-Item -ItemType Directory -Path $zipDir -Force | Out-Null }
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$publishDir = [System.IO.Path]::GetFullPath((Join-Path $tempRoot "oficina-auth-publish-$PID"))
if (-not $publishDir.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Caminho temporário inválido.' }
$dotnet = $DotnetPath
if (-not $dotnet) {
    $dotnet = Join-Path $repoRoot '.dotnet_home/sdk10/dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
}
if (-not (Test-Path -LiteralPath $dotnet)) { throw "dotnet não encontrado: $dotnet" }

try {
    & $dotnet publish (Join-Path $repoRoot 'services/Oficina.Auth/Oficina.Auth.csproj') -c Release -o $publishDir --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar Oficina.Auth.' }
    if (-not (Test-Path -LiteralPath (Join-Path $publishDir 'Oficina.Auth.dll'))) { throw 'Oficina.Auth.dll ausente.' }
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -Force
    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        if (-not ($zip.Entries | Where-Object { $_.FullName -eq 'Oficina.Auth.dll' })) { throw 'ZIP sem DLL da Function.' }
    } finally { $zip.Dispose() }
    "Pacote: $zipPath"
    "SHA256: $((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant())"
}
finally {
    if (Test-Path -LiteralPath $publishDir) {
        $verified = [System.IO.Path]::GetFullPath($publishDir)
        if (-not $verified.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Remoção temporária fora do caminho esperado.' }
        Remove-Item -LiteralPath $verified -Recurse -Force
    }
}
