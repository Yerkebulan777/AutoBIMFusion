Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Join-Path (Split-Path $PSScriptRoot -Parent) ('out\compatibility\publication-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'source'
$target = Join-Path $root 'installed\AutoBIMFusion.bundle'
$publish = Join-Path $PSScriptRoot 'Publish-AutoCADBundle.ps1'
New-Item -ItemType Directory -Path (Join-Path $source 'Contents') -Force | Out-Null
'<ApplicationPackage><Components><ComponentEntry ModuleName="./Contents/AutoBIMFusion.dll" /></Components></ApplicationPackage>' |
    Set-Content -LiteralPath (Join-Path $source 'PackageContents.xml')
foreach ($name in @('AutoBIMFusion', 'AutoBIMFusion.Common', 'AutoBIMFusion.Merge', 'Serilog', 'Serilog.Sinks.File')) {
    'old' | Set-Content -LiteralPath (Join-Path $source "Contents\$name.dll")
}
& $publish -SourceBundle $source -TargetBundle $target
$targetDll = Join-Path $target 'Contents\AutoBIMFusion.dll'
$sourceDll = Join-Path $source 'Contents\AutoBIMFusion.dll'
'obsolete' | Set-Content -LiteralPath (Join-Path $target 'Contents\obsolete.dll')
'new' | Set-Content -LiteralPath $sourceDll
& $publish -SourceBundle $source -TargetBundle $target
if ((Get-Content -LiteralPath $targetDll) -ne 'new' -or (Test-Path -LiteralPath (Join-Path $target 'Contents\obsolete.dll'))) {
    throw 'Replacement retained stale files or lost new files.'
}

# A partial source must never touch the current installation.
Remove-Item -LiteralPath $sourceDll
$failed = $false
try { & $publish -SourceBundle $source -TargetBundle $target } catch { $failed = $true }
if (-not $failed -or (Get-Content -LiteralPath $targetDll) -ne 'new') { throw 'Incomplete source damaged installation.' }
'next' | Set-Content -LiteralPath $sourceDll

# A running host that denies file sharing must not leave a partial installation.
$held = [IO.File]::Open($targetDll, 'Open', 'Read', 'None')
$failed = $false
try {
    try { & $publish -SourceBundle $source -TargetBundle $target } catch { $failed = $true }
}
finally { $held.Dispose() }
$expected = if ($failed) { 'new' } else { 'next' }
if ((Get-Content -LiteralPath $targetDll) -ne $expected) { throw 'Locked-file publication damaged installation.' }
foreach ($name in @('AutoBIMFusion.Common', 'AutoBIMFusion.Merge', 'Serilog', 'Serilog.Sinks.File')) {
    if (-not (Test-Path -LiteralPath (Join-Path $target "Contents\$name.dll"))) { throw "Lost $name.dll" }
}

# Concurrent installers must fail before touching the installed package.
$held = [IO.File]::Open((Join-Path (Split-Path $target -Parent) '.AutoBIMFusion-install.lock'), 'Open', 'ReadWrite', 'None')
$failed = $false
try {
    try { & $publish -SourceBundle $source -TargetBundle $target } catch { $failed = $true }
}
finally { $held.Dispose() }
if (-not $failed -or (Get-Content -LiteralPath $targetDll) -ne $expected) { throw 'Concurrent publication was not rejected safely.' }
Write-Host "PASS: replacement, incomplete source, locked DLL, concurrent publisher. Artifacts: $root"
