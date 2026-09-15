param(
    [Parameter(Mandatory = $true)][string]$PayloadDir,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][ValidateRange(2019, 2027)][int]$Year
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'New-InstallerPayload.ps1')

Assert-InstallerPayload -Destination $PayloadDir -Version $Version -Year $Year
Write-Host "Verified installer payload: managed DLLs and autoload registration for AutoCAD $Year."

