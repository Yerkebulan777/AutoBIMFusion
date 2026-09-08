function Get-InstalledBatchAutoCAD {
    param([string]$AutoCADRoot)

    $roots = @()
    if ($AutoCADRoot) {
        $roots = @($AutoCADRoot)
    }
    else {
        # Registry discovery also covers installations outside Program Files and verticals.
        foreach ($registryRoot in @('HKLM:\SOFTWARE\Autodesk\AutoCAD', 'HKCU:\SOFTWARE\Autodesk\AutoCAD')) {
            if (Test-Path $registryRoot) {
                foreach ($key in Get-ChildItem $registryRoot -Recurse -ErrorAction SilentlyContinue) {
                    $location = $key.GetValue('AcadLocation')
                    if ($location) { $roots += $location }
                }
            }
        }
        if ($env:ACAD_HOME) { $roots += $env:ACAD_HOME }
        $autodeskRoot = Join-Path $env:ProgramFiles 'Autodesk'
        if (Test-Path -LiteralPath $autodeskRoot) {
            $roots += @(Get-ChildItem -LiteralPath $autodeskRoot -Directory -Filter 'AutoCAD*' |
                Select-Object -ExpandProperty FullName)
        }
    }

    $years = @{ '23.0' = 2019; '23.1' = 2020; '24.0' = 2021; '24.1' = 2022;
        '24.2' = 2023; '24.3' = 2024; '25.0' = 2025; '25.1' = 2026; '26.0' = 2027 }
    foreach ($root in $roots | Sort-Object -Unique) {
        $exe = Join-Path $root 'acad.exe'
        if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { continue }
        # FileVersion and ProductVersion differ (e.g. 29.0 vs 23.0 in AutoCAD 2019).
        $version = (Get-Item -LiteralPath $exe).VersionInfo
        $series = '{0}.{1}' -f $version.ProductMajorPart, $version.ProductMinorPart
        if ($years.ContainsKey($series)) {
            [pscustomobject]@{ Exe = $exe; Year = $years[$series]; Series = [version]$series }
        }
    }
}

function Find-MergeDwgBatchHost {
    param(
        [object[]]$Installations,
        [string[]]$ApplicationPluginsRoots,
        [string]$Configuration
    )

    $plugins = @()
    foreach ($root in $ApplicationPluginsRoots) {
        $bundle = Join-Path $root 'AutoBIMFusion.bundle'
        $manifest = Join-Path $bundle 'PackageContents.xml'
        if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) { continue }
        try {
            [xml]$xml = Get-Content -LiteralPath $manifest -Raw
            foreach ($components in $xml.SelectNodes('/ApplicationPackage/Components')) {
                $requirements = $components.SelectSingleNode('RuntimeRequirements')
                if ($null -eq $requirements) { continue }
                if ($requirements.GetAttribute('OS') -ne 'Win64' -or
                    $requirements.GetAttribute('Platform') -notlike 'AutoCAD*') { continue }
                $min = [version]($requirements.GetAttribute('SeriesMin') -replace '^R', '')
                $max = [version]($requirements.GetAttribute('SeriesMax') -replace '^R', '')
                foreach ($entry in $components.SelectNodes('ComponentEntry')) {
                    if ($entry.GetAttribute('AppName') -ne 'AutoBIMFusion') { continue }
                    $dll = [IO.Path]::GetFullPath((Join-Path $bundle $entry.GetAttribute('ModuleName')))
                    $contents = Split-Path $dll -Parent
                    $missing = @(@($dll) + @('AutoBIMFusion.Common.dll', 'AutoBIMFusion.Merge.dll',
                        'Serilog.dll', 'Serilog.Sinks.File.dll' | ForEach-Object { Join-Path $contents $_ }) |
                        Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) })
                    if ($missing.Count -gt 0) { continue }
                    $plugins += [pscustomobject]@{ Path = $dll; Min = $min; Max = $max }
                }
            }
        }
        catch {
            Write-Warning "Cannot use installed bundle ${manifest}: $($_.Exception.Message)"
        }
    }

    foreach ($installation in $Installations | Sort-Object @{ Expression = 'Year'; Descending = $true }, Exe) {
        if (-not (Test-Path -LiteralPath $installation.Exe -PathType Leaf)) { continue }
        if ($Configuration -and $installation.Year -ne (2000 + [int]$Configuration.Substring($Configuration.Length - 2))) {
            continue
        }
        foreach ($plugin in $plugins) {
            if ($installation.Series -ge $plugin.Min -and $installation.Series -le $plugin.Max) {
                return [pscustomobject]@{ Exe = $installation.Exe; Year = $installation.Year; PluginPath = $plugin.Path }
            }
        }
    }
    throw 'No installed AutoCAD 2019-2027 with a compatible AutoBIMFusion bundle was found. Check acad.exe, PackageContents.xml (SeriesMin/SeriesMax) and the plugin DLLs in Autodesk\ApplicationPlugins. Explicit AutoCADRoot/Configuration must also match.'
}
