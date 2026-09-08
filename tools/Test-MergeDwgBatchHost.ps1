Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'MergeDwgBatchHost.ps1')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('AutoBIMFusion-host-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null

function New-TestBundle {
    param([string]$Name, [string]$Min, [string]$Max)
    $root = Join-Path $testRoot $Name
    $bundle = Join-Path $root 'AutoBIMFusion.bundle'
    $contents = Join-Path $bundle 'Contents'
    New-Item -ItemType Directory -Path $contents -Force | Out-Null
    foreach ($name in @('AutoBIMFusion', 'AutoBIMFusion.Common', 'AutoBIMFusion.Merge', 'Serilog', 'Serilog.Sinks.File')) {
        'fixture' | Set-Content -LiteralPath (Join-Path $contents "$name.dll")
    }
    @"
<ApplicationPackage><Components>
<RuntimeRequirements OS="Win64" Platform="AutoCAD" SeriesMin="$Min" SeriesMax="$Max" />
<ComponentEntry AppName="AutoBIMFusion" ModuleName="./Contents/AutoBIMFusion.dll" />
</Components></ApplicationPackage>
"@ | Set-Content -LiteralPath (Join-Path $bundle 'PackageContents.xml')
    return $root
}

function Assert-Selection {
    param([object[]]$Hosts, [string[]]$Roots, [int]$Year, [string]$Configuration)
    $selected = Find-MergeDwgBatchHost -Installations $Hosts -ApplicationPluginsRoots $Roots -Configuration $Configuration
    if ($selected.Year -ne $Year) { throw "Expected AutoCAD $Year; got $($selected.Year)." }
    return $selected
}

function Assert-NoSelection {
    param([object[]]$Hosts, [string[]]$Roots, [string]$Configuration)
    try {
        $null = Find-MergeDwgBatchHost -Installations $Hosts -ApplicationPluginsRoots $Roots -Configuration $Configuration
    }
    catch {
        if ($_.Exception.Message -like 'No installed AutoCAD*') { return }
        throw
    }
    throw 'Expected failure when no compatible installed pair exists.'
}

$hosts = @(foreach ($item in @(@(2019, '23.0'), @(2026, '25.1'), @(2027, '26.0'))) {
    $exe = Join-Path $testRoot "acad-$($item[0]).exe"
    'fixture' | Set-Content -LiteralPath $exe
    [pscustomobject]@{ Exe = $exe; Year = $item[0]; Series = [version]$item[1] }
})
$legacy = New-TestBundle 'user' 'R23.0' 'R23.0'
$modern = New-TestBundle 'machine' 'R25.1' 'R25.1'
$latest = New-TestBundle 'program-files' 'R26.0' 'R26.0'

# Newest executable without a matching bundle must not win.
$null = Assert-Selection $hosts @($legacy, $modern) 2026
# Registry enumeration order and bundle location must not affect year priority.
$null = Assert-Selection @($hosts[1], $hosts[2], $hosts[0]) @($legacy, $modern, $latest) 2027
$selected = Assert-Selection $hosts @($legacy) 2019
if ($selected.PluginPath -ne (Join-Path $legacy 'AutoBIMFusion.bundle\Contents\AutoBIMFusion.dll')) {
    throw 'Selection did not return the installed DLL.'
}
$null = Assert-Selection $hosts @($legacy, $modern, $latest) 2019 'ReleaseA19'
Assert-NoSelection $hosts @($legacy) 'DebugA27'
Assert-NoSelection @() @($legacy)
Assert-NoSelection $hosts @()

# A missing dependency disqualifies the newest bundle; malformed XML also falls back.
Remove-Item -LiteralPath (Join-Path $latest 'AutoBIMFusion.bundle\Contents\AutoBIMFusion.Merge.dll')
$null = Assert-Selection $hosts @($latest, $modern, $legacy) 2026
'<broken' | Set-Content -LiteralPath (Join-Path $latest 'AutoBIMFusion.bundle\PackageContents.xml')
$null = Assert-Selection $hosts @($latest, $legacy) 2019

# Missing primary DLL and executable must be rejected as well.
Remove-Item -LiteralPath (Join-Path $modern 'AutoBIMFusion.bundle\Contents\AutoBIMFusion.dll')
$null = Assert-Selection $hosts @($modern, $legacy) 2019
Remove-Item -LiteralPath $hosts[0].Exe
Assert-NoSelection $hosts @($legacy)

# Both manifest bounds are inclusive.
$range = New-TestBundle 'range' 'R25.0' 'R26.0'
$null = Assert-Selection $hosts @($range) 2027
$null = Assert-Selection @($hosts[1]) @($range) 2026
Assert-NoSelection @($hosts[1]) @($legacy)

# An explicit nonexistent directory must not fall back to another installation.
$explicit = @(Get-InstalledBatchAutoCAD -AutoCADRoot (Join-Path $testRoot 'not-installed'))
if ($explicit.Count -ne 0) { throw 'Explicit AutoCADRoot was ignored.' }
Write-Host "PASS: newest compatible host, installed DLL, missing files, malformed manifest, version bounds, explicit filters. Fixtures: $testRoot"
