<#
.SYNOPSIS
    Runs Roslynator analyze on all three AutoBIMFusion projects.
    Saves output to tools/roslynator-results.txt.

.EXAMPLE
    .\tools\Find-DeadCode.ps1
    .\tools\Find-DeadCode.ps1 -Configuration DebugA27
#>

param(
    [string]$Configuration = "DebugA26"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot   = Split-Path -Parent $scriptRoot
$outputFile = Join-Path $scriptRoot "roslynator-results.txt"

$projects = @(
    "src\AutoBIMFusion.Common\AutoBIMFusion.Common.csproj",
    "src\AutoBIMFusion.Merge\AutoBIMFusion.Merge.csproj",
    "src\AutoBIMFusion.Plugin\AutoBIMFusion.Plugin.csproj"
)

# Install roslynator if missing
$cmd = Get-Command roslynator -ErrorAction SilentlyContinue
if ($null -eq $cmd) {
    Write-Host "Installing roslynator..." -ForegroundColor Yellow
    dotnet tool install -g roslynator.dotnet.cli
    if ($LASTEXITCODE -ne 0) { Write-Error "Install failed."; exit 1 }
    Write-Host "Installed." -ForegroundColor Green
}

if (Test-Path $outputFile) { Remove-Item $outputFile }

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm"
Add-Content $outputFile "Roslynator analyze — $timestamp | Config: $Configuration"
Add-Content $outputFile ("=" * 60)

foreach ($proj in $projects) {
    $projPath = Join-Path $repoRoot $proj
    Write-Host "Analyzing $proj ..." -ForegroundColor Cyan

    Add-Content $outputFile ""
    Add-Content $outputFile "### $proj"

    $output = roslynator analyze $projPath --severity-level info 2>&1
    $output | Add-Content $outputFile

    if ($LASTEXITCODE -ne 0) {
        Write-Warning "roslynator returned non-zero for $proj"
    }
}

Write-Host ""
Write-Host "Results: $outputFile" -ForegroundColor Green
Write-Host "Cross-reference with: tools\dead-code-candidates.md" -ForegroundColor Yellow
