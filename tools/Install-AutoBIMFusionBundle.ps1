param(
    [ValidatePattern('^(Debug|Release)A(19|2[0-7])$')]
    [string]$Configuration = 'DebugA26'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
# Always build desktop: an existing bundle may be stale or produced with different options.
& dotnet build (Join-Path $repoRoot 'AutoBIMFusion.slnx') -c $Configuration '-p:CoreConsoleDiagnostics=false' '-p:DisableAutoCADDeployment=true'
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
$settings = & (Join-Path $PSScriptRoot 'Get-AutoCADBuildSettings.ps1') -Configuration $Configuration
& (Join-Path $PSScriptRoot 'Publish-AutoCADBundle.ps1') `
    -SourceBundle (Join-Path $settings.TargetDir 'AutoBIMFusion.bundle') `
    -TargetBundle (Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\AutoBIMFusion.bundle')
Write-Host 'Restart AutoCAD and run MERGEDWG.'
