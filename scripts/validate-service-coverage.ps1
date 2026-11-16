param(
    [Parameter(Mandatory = $true)][string]$AssemblyName,
    [int]$Minimum = 80,
    [Parameter(Mandatory = $true)][string]$UnitResultsDirectory,
    [Parameter(Mandatory = $true)][string]$IntegrationResultsDirectory
)

$ErrorActionPreference = 'Stop'

function Read-ServiceLines([string]$directory) {
    $reports = @(Get-ChildItem -LiteralPath $directory -Recurse -Filter 'coverage.cobertura.xml')
    if ($reports.Count -eq 0) { throw "Nenhum relatório Cobertura em $directory." }
    $hashes = @($reports | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } | Select-Object -Unique)
    if ($hashes.Count -ne 1) { throw "Relatórios Cobertura diferentes em $directory; encontrados $($reports.Count)." }
    [xml]$coverage = Get-Content -LiteralPath $reports[0].FullName
    $packages = @($coverage.coverage.packages.package | Where-Object { $_.name -eq $AssemblyName })
    if ($packages.Count -ne 1) { throw "Pacote $AssemblyName ausente ou duplicado em $directory." }
    $lines = @{}
    foreach ($class in @($packages[0].classes.class)) {
        foreach ($line in @($class.lines.line)) {
            $key = "$($class.filename):$($line.number)"
            $hit = [int]$line.hits -gt 0
            if ($lines.ContainsKey($key)) { $lines[$key] = $lines[$key] -or $hit }
            else { $lines[$key] = $hit }
        }
    }
    if ($lines.Count -eq 0) { throw "Nenhuma linha instrumentada de $AssemblyName em $directory." }
    return $lines
}

$unit = Read-ServiceLines $UnitResultsDirectory
$integration = Read-ServiceLines $IntegrationResultsDirectory
if ($unit.Count -ne $integration.Count) { throw 'As suítes instrumentaram conjuntos diferentes de linhas.' }
$covered = 0
foreach ($key in $unit.Keys) {
    if (-not $integration.ContainsKey($key)) { throw "Linha instrumentada ausente na integração: $key" }
    if ($unit[$key] -or $integration[$key]) { $covered++ }
}
$percent = [math]::Round(100.0 * $covered / $unit.Count, 2)
Write-Output "Cobertura $AssemblyName`: $covered/$($unit.Count) linhas ($percent%). Mínimo: $Minimum%."
if ($covered * 100 -lt $Minimum * $unit.Count) { throw "Cobertura $AssemblyName abaixo de $Minimum%." }
