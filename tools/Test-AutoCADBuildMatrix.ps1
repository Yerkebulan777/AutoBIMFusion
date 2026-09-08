param(
    [ValidateSet('Debug', 'Release')]
    [string[]]$BuildTypes = @('Debug', 'Release'),
    [ValidateRange(2019, 2027)]
    [int[]]$Years = @(2019..2027)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path $repoRoot 'out\compatibility'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$results = @()
foreach ($buildType in $BuildTypes) {
    foreach ($year in $Years) {
        $configuration = "${buildType}A$($year - 2000)"
        foreach ($headless in @($false, $true)) {
            $mode = if ($headless) { 'headless' } else { 'desktop' }
            $logPath = Join-Path $outputRoot "$configuration-$mode.log"
            $properties = @("-p:CoreConsoleDiagnostics=$($headless.ToString().ToLowerInvariant())",
                "-p:AutoCADUserPluginsDir=$outputRoot\deploy\")
            & dotnet build (Join-Path $repoRoot 'AutoBIMFusion.slnx') -c $configuration @properties -v:q *> $logPath
            if ($LASTEXITCODE -ne 0) {
                Get-Content -LiteralPath $logPath -Tail 30
                throw "Build failed: $configuration $mode"
            }

            $project = Join-Path $repoRoot 'src\AutoBIMFusion.Plugin\AutoBIMFusion.Plugin.csproj'
            $output = & dotnet msbuild $project "-p:Configuration=$configuration" '-p:Platform=x64' @properties '-getProperty:TargetDir,TargetFramework,AutoCADSeries,IntermediateOutputPath'
            if ($LASTEXITCODE -ne 0) { throw "Property evaluation failed: $configuration" }
            $settings = ($output -join [Environment]::NewLine | ConvertFrom-Json).Properties
            $bundle = Join-Path $settings.TargetDir 'AutoBIMFusion.bundle'
            [xml]$manifest = Get-Content -LiteralPath (Join-Path $bundle 'PackageContents.xml') -Raw
            $component = $manifest.ApplicationPackage.Components
            $expectedSeries = switch ($year) {
                2019 { 'R23.0' } 2020 { 'R23.1' } 2021 { 'R24.0' }
                2022 { 'R24.1' } 2023 { 'R24.2' } 2024 { 'R24.3' }
                2025 { 'R25.0' } 2026 { 'R25.1' } 2027 { 'R26.0' }
            }
            if ($component.RuntimeRequirements.SeriesMin -ne $expectedSeries -or
                $component.RuntimeRequirements.SeriesMax -ne $expectedSeries -or
                $component.ComponentEntry.ModuleName -ne './Contents/AutoBIMFusion.dll') {
                throw "Unexpected autoload manifest: $bundle"
            }
            foreach ($name in @('AutoBIMFusion.dll', 'AutoBIMFusion.Common.dll', 'AutoBIMFusion.Merge.dll', 'Serilog.dll', 'Serilog.Sinks.File.dll')) {
                if (-not (Test-Path -LiteralPath (Join-Path $bundle "Contents\$name"))) {
                    throw "Missing runtime dependency: $name ($configuration $mode)"
                }
            }
            $hostDlls = @(Get-ChildItem -LiteralPath (Join-Path $bundle 'Contents') -Filter '*.dll' |
                Where-Object Name -Match '^(acmgd|acdbmgd|accoremgd|AdWindows|AcWindows|Autodesk\.|Aecc|AecBase)')
            if ($hostDlls.Count -gt 0) { throw "Host DLLs in bundle: $($hostDlls.Name -join ', ')" }
            if ($headless -and (Test-Path -LiteralPath (Join-Path $bundle 'Contents\Resources'))) {
                throw "Headless bundle contains Ribbon resources."
            }
            if (-not $headless) {
                $desktopSettings = $settings
                $desktopDll = Join-Path $bundle 'Contents\AutoBIMFusion.dll'
                $desktopHash = (Get-FileHash -LiteralPath $desktopDll).Hash
                $deployedDll = Join-Path $outputRoot 'deploy\AutoBIMFusion.bundle\Contents\AutoBIMFusion.dll'
                if ((Get-FileHash -LiteralPath $deployedDll).Hash -ne $desktopHash) {
                    throw 'Desktop installation does not match the built bundle.'
                }
                if (-not (Test-Path -LiteralPath (Join-Path $bundle 'Contents\Resources'))) {
                    throw 'Desktop bundle is missing Ribbon resources.'
                }
            }
            elseif ($settings.TargetDir -eq $desktopSettings.TargetDir -or
                $settings.IntermediateOutputPath -eq $desktopSettings.IntermediateOutputPath -or
                (Get-FileHash -LiteralPath $desktopDll).Hash -ne $desktopHash -or
                (Get-FileHash -LiteralPath $deployedDll).Hash -ne $desktopHash) {
                throw 'Headless build overwrote desktop artifacts or shares intermediate files.'
            }
            $results += [pscustomobject]@{ Configuration = $configuration; Mode = $mode; Framework = $settings.TargetFramework; Series = $expectedSeries; Result = 'PASS' }
            Write-Host "PASS $configuration $mode ($($settings.TargetFramework), $expectedSeries)"
        }
    }
}
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot 'matrix-results.json') -Encoding UTF8
