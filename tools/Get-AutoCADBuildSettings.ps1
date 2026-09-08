param(
    [ValidatePattern('^(Debug|Release)A(19|2[0-7])$')]
    [string]$Configuration = 'DebugA26'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$project = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\AutoBIMFusion.Plugin\AutoBIMFusion.Plugin.csproj'
$output = & dotnet msbuild $project "-p:Configuration=$Configuration" '-p:Platform=x64' '-getProperty:TargetFramework,TargetDir,TargetPath,AcadVersion,AutoCADSeries'
if ($LASTEXITCODE -ne 0) {
    throw "Cannot evaluate AutoCAD build settings for $Configuration."
}

($output -join [Environment]::NewLine | ConvertFrom-Json).Properties
