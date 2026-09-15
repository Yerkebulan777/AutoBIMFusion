Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'New-InstallerPayload.ps1')
. (Join-Path $PSScriptRoot 'TestFixtures\InstallerBundle.ps1')

$root = Join-Path (Split-Path $PSScriptRoot -Parent) ('out\installer-payload-test-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $root 'AutoBIMFusion.bundle'
$manifestPath = Join-Path $payload 'PackageContents.xml'
foreach ($year in 2019..2027) {
    $source = New-InstallerTestBundle -Root $root -Year $year
    [void](New-InstallerPayload -SourceBundle $source -Destination $payload -Version '1.2.3' -Year $year)
    [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
    if ($manifest.ApplicationPackage.Name -ne "AutoBIMFusion $year" -or
        $manifest.ApplicationPackage.ProductCode -ne "{4F3E42D4-4D2F-4A2E-8A2A-BB771A0D$year}" -or
        $manifest.ApplicationPackage.Components.ComponentEntry.ModuleName -ne "./Contents/$year/AutoBIMFusion.dll") {
        throw "Payload did not receive final AutoCAD $year identity."
    }
    $directories = @(Get-ChildItem -LiteralPath (Join-Path $payload 'Contents') -Directory)
    if ($directories.Count -ne 1 -or $directories[0].Name -ne [string]$year) { throw 'Replacement retained old year files.' }
}

# Bad source must preserve the complete previously published package, including its identity.
$hashes = @{}
foreach ($file in Get-ChildItem -LiteralPath $payload -File -Recurse) {
    $hashes[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName).Hash
}
$source = New-InstallerTestBundle -Root $root -Year 2020
$sourceManifestPath = Join-Path $source 'PackageContents.xml'
$originalXml = Get-Content -LiteralPath $sourceManifestPath -Raw
$cases = @(
    @{ XPath = '/ApplicationPackage/Components/RuntimeRequirements'; Attribute = 'SeriesMin'; Value = 'R25.1' },
    @{ XPath = '/ApplicationPackage/Components/ComponentEntry/RuntimeRequirements'; Attribute = 'SeriesMax'; Value = 'R25.1' },
    @{ XPath = '/ApplicationPackage/Components/ComponentEntry/RuntimeRequirements'; Attribute = 'OS'; Value = 'Win32' },
    @{ XPath = '/ApplicationPackage/Components/ComponentEntry'; Attribute = 'LoadOnAutoCADStartup'; Value = 'false' },
    @{ XPath = '/ApplicationPackage/Components/ComponentEntry'; Attribute = 'AppName'; Value = 'OtherPlugin' },
    @{ XPath = '/ApplicationPackage/Components/ComponentEntry'; Attribute = 'AppType'; Value = '' }
)
foreach ($case in $cases) {
    [xml]$broken = $originalXml
    $broken.SelectSingleNode($case.XPath).SetAttribute($case.Attribute, $case.Value)
    $broken.Save($sourceManifestPath)
    $failed = $false
    try { [void](New-InstallerPayload -SourceBundle $source -Destination $payload -Version '1.2.4' -Year 2020) }
    catch { $failed = $true }
    if (-not $failed) { throw "Accepted invalid source: $($case.Attribute)=$($case.Value)" }
}
$originalXml | Set-Content -LiteralPath $sourceManifestPath
foreach ($badName in @('acmgd.dll', 'ExtraDependency.dll', 'AutoBIMFusion.dll')) {
    $badPath = Join-Path $source "Contents\$badName"
    'MZ-not-a-managed-assembly' | Set-Content -LiteralPath $badPath
    $failed = $false
    try { [void](New-InstallerPayload -SourceBundle $source -Destination $payload -Version '1.2.4' -Year 2020) }
    catch { $failed = $true }
    if (-not $failed) { throw "Accepted invalid DLL: $badName" }
    Remove-Item -LiteralPath $badPath
}
$files = @(Get-ChildItem -LiteralPath $payload -File -Recurse)
if ($files.Count -ne $hashes.Count) { throw 'Rejected source changed published file count.' }
foreach ($file in $files) {
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne $hashes[$file.FullName]) { throw "Rejected source changed $($file.FullName)." }
}
if (@(Get-ChildItem -LiteralPath $root -Directory -Filter '.AutoBIMFusion-*').Count -ne 0) {
    throw 'Payload preparation left temporary directories.'
}
Write-Host "PASS: all nine year identities, replacement without stale files, invalid registration/DLL rejection preserves previous payload. Artifacts: $root"
