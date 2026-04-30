param(
    [double]$Minimum = 80
)

$ErrorActionPreference = "Stop"

$projects = @(
    @{
        Name = "OficinaMvp.Domain.Tests"
        ProjectPath = "tests\OficinaMvp.Domain.Tests\OficinaMvp.Domain.Tests.csproj"
        TestResultsPath = "tests\OficinaMvp.Domain.Tests\TestResults"
        ScopeDescription = "Domain.*"
        IncludeClassRegex = "^OficinaMvp\.Api\.Domain\."
        IncludeFileRegex = "Domain[\\/]"
    },
    @{
        Name = "OficinaMvp.Integration.Tests"
        ProjectPath = "tests\OficinaMvp.Integration.Tests\OficinaMvp.Integration.Tests.csproj"
        TestResultsPath = "tests\OficinaMvp.Integration.Tests\TestResults"
        ScopeDescription = "Domain.Entities.* + Domain.Exceptions.*"
        IncludeClassRegex = "^OficinaMvp\.Api\.Domain\.(Entities|Exceptions)\."
        IncludeFileRegex = "Domain[\\/](Entities|Exceptions)[\\/]"
    }
)

$failed = @()

foreach ($project in $projects) {
    Write-Host "==> Running $($project.Name) with coverage collection..."
    Remove-Item -Recurse -Force $project.TestResultsPath -ErrorAction SilentlyContinue

    dotnet test $project.ProjectPath --collect:"XPlat Code Coverage" --verbosity minimal -m:1
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed for $($project.Name)."
    }

    $coverageFile = Get-ChildItem -Path $project.TestResultsPath -Recurse -Filter "coverage.cobertura.xml" |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if (-not $coverageFile) {
        throw "Coverage file not found for $($project.Name)."
    }

    [xml]$coverageXml = Get-Content -Path $coverageFile.FullName -Raw
    $classes = @($coverageXml.SelectNodes("//class"))

    $domainClasses = $classes | Where-Object {
        $_.name -match $project.IncludeClassRegex -or
        $_.filename -match $project.IncludeFileRegex
    }

    $totalLines = 0
    $coveredLines = 0

    foreach ($class in $domainClasses) {
        $lines = @($class.SelectNodes("./lines/line"))
        foreach ($line in $lines) {
            $totalLines++
            if ([int]$line.hits -gt 0) {
                $coveredLines++
            }
        }
    }

    if ($totalLines -eq 0) {
        throw "No domain lines found in coverage for $($project.Name)."
    }

    $coverage = [math]::Round(($coveredLines / $totalLines) * 100, 2)
    Write-Host "$($project.Name) [$($project.ScopeDescription)] coverage: $coveredLines/$totalLines ($coverage%)"

    if ($coverage -lt $Minimum) {
        $failed += "$($project.Name): $coverage% (< $Minimum%)"
    }
}

if ($failed.Count -gt 0) {
    Write-Error ("Domain coverage threshold not met: " + ($failed -join "; "))
    exit 1
}

Write-Host "Domain coverage threshold met for all test projects (>= $Minimum%)."
exit 0
