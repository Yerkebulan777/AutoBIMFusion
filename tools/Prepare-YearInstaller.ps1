param(
    [Parameter(Mandatory = $true)][ValidateRange(2019, 2027)][int]$Year,
    [ValidateSet('Debug', 'Release')][string]$BuildType = 'Release',
    [Parameter(Mandatory = $true)][string]$PayloadDir,
    [Parameter(Mandatory = $true)][string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'New-InstallerPayload.ps1')
$repoRoot = Split-Path $PSScriptRoot -Parent
$configuration = "$BuildType`A$($Year - 2000)"
& dotnet build (Join-Path $repoRoot 'src\AutoBIMFusion.Plugin\AutoBIMFusion.Plugin.csproj') `
    --configuration $configuration --nologo '-p:Platform=x64' '-p:CoreConsoleDiagnostics=false' `
    '-p:DisableAutoCADDeployment=true' '-p:RunAnalyzersDuringBuild=false' '-p:EnforceCodeStyleInBuild=false'
if ($LASTEXITCODE -ne 0) { throw "Plugin build failed for AutoCAD $Year." }

$settings = & (Join-Path $PSScriptRoot 'Get-AutoCADBuildSettings.ps1') -Configuration $configuration
[void](New-InstallerPayload -SourceBundle (Join-Path $settings.TargetDir 'AutoBIMFusion.bundle') `
    -Destination $PayloadDir -Version $Version -Year $Year)
