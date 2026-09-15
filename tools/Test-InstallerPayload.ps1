Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'New-InstallerPayload.ps1')
. (Join-Path $PSScriptRoot 'MergeDwgBatchHost.ps1')

$root = Join-Path ([IO.Path]::GetTempPath()) ('AutoBIMFusion-installer-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null

try {
    $yearBundles = @{}
    foreach ($year in 2019..2027) { $yearBundles[$year] = New-InstallerTestBundle -Root $root -Year $year }

    $payload = Join-Path $root 'AutoBIMFusion.bundle'
    [void](New-InstallerPayload -Destination $payload -Version '1.2.3' -YearBundles $yearBundles)

    [xml]$manifest = Get-Content -LiteralPath (Join-Path $payload 'PackageContents.xml') -Raw -Encoding UTF8
    $first = $manifest.SelectSingleNode('/ApplicationPackage/Components')
    $firstEntry = $first.SelectSingleNode('ComponentEntry')
    if ($first.SelectSingleNode('RuntimeRequirements').GetAttribute('Platform') -ne 'AutoCAD*' -or
        $firstEntry.GetAttribute('ModuleName') -ne './Contents/2019/AutoBIMFusion.dll' -or
        $firstEntry.GetAttribute('AppType') -ne '.Net' -or
        $firstEntry.SelectSingleNode('RuntimeRequirements').GetAttribute('Platform') -ne 'AutoCAD*') {
        throw 'Installer payload did not remap the plugin PackageContents into year folders.'
    }

    $incompleteAutoload = New-InstallerTestBundle -Root (Join-Path $root 'no-apptype') -Year 2019
    [xml]$broken = Get-Content -LiteralPath (Join-Path $incompleteAutoload 'PackageContents.xml') -Raw
    $broken.SelectSingleNode('/ApplicationPackage/Components/ComponentEntry').RemoveAttribute('AppType')
    $broken.Save((Join-Path $incompleteAutoload 'PackageContents.xml'))
    try {
        New-InstallerPayload -Destination (Join-Path $root 'no-apptype\AutoBIMFusion.bundle') -Version '1.0.0' -YearBundles @{ 2019 = $incompleteAutoload }
        throw 'Missing AppType check failed.'
    }
    catch {
        if ($_.Exception.Message -notlike '*AppType=.Net*') { throw }
    }

    $hosts = @(
        [pscustomobject]@{ Exe = (Join-Path $root 'acad-2019.exe'); Year = 2019; Series = [version]'23.0' }
        [pscustomobject]@{ Exe = (Join-Path $root 'acad-2026.exe'); Year = 2026; Series = [version]'25.1' }
        [pscustomobject]@{ Exe = (Join-Path $root 'acad-2027.exe'); Year = 2027; Series = [version]'26.0' }
    )
    foreach ($item in $hosts) { 'fixture' | Set-Content -LiteralPath $item.Exe }

    $selected = Find-MergeDwgBatchHost -Installations $hosts -ApplicationPluginsRoots @((Split-Path $payload -Parent))
    if ($selected.Year -ne 2027 -or $selected.PluginPath -notlike '*\Contents\2027\AutoBIMFusion.dll') {
        throw "Expected AutoCAD 2027 payload DLL; got Year=$($selected.Year) Path=$($selected.PluginPath)"
    }
    $selected = Find-MergeDwgBatchHost -Installations $hosts -ApplicationPluginsRoots @((Split-Path $payload -Parent)) -Configuration 'ReleaseA19'
    if ($selected.PluginPath -notlike '*\Contents\2019\AutoBIMFusion.dll') {
        throw "Expected AutoCAD 2019 payload DLL; got $($selected.PluginPath)"
    }

    try {
        New-InstallerPayload -Destination (Join-Path $root 'wrong-name') -Version '1.0.0' -YearBundles @{ 2019 = $yearBundles[2019] }
        throw 'Destination name check failed.'
    }
    catch {
        if ($_.Exception.Message -notlike 'Payload destination must be named AutoBIMFusion.bundle.') { throw }
    }

    $hostDllBundle = New-InstallerTestBundle -Root (Join-Path $root 'host-source') -Year 2019
    'fake' | Set-Content -LiteralPath (Join-Path $hostDllBundle 'Contents\acmgd.dll')
    try {
        New-InstallerPayload -Destination (Join-Path $root 'host-dll\AutoBIMFusion.bundle') -Version '1.0.0' -YearBundles @{ 2019 = $hostDllBundle }
        throw 'Host DLL check failed.'
    }
    catch {
        if ($_.Exception.Message -notlike 'Host DLLs in AutoCAD 2019 bundle:*') { throw }
    }

    Write-Host "PASS: plugin PackageContents remap, batch host year folders, validation. Fixtures: $root"
}
finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
}
