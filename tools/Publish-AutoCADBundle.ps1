param(
    [Parameter(Mandatory = $true)][string]$SourceBundle,
    [Parameter(Mandatory = $true)][string]$TargetBundle
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $SourceBundle).ProviderPath.TrimEnd('\')
$target = [IO.Path]::GetFullPath($TargetBundle).TrimEnd('\')
if ([IO.Path]::GetFileName($target) -ne 'AutoBIMFusion.bundle' -or $source -eq $target -or
    $target.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publication requires a separate target named AutoBIMFusion.bundle.'
}
$parent = [IO.Path]::GetDirectoryName($target)
New-Item -ItemType Directory -Path $parent -Force | Out-Null
$id = [Guid]::NewGuid().ToString('N')
$stage = Join-Path $parent ('.AutoBIMFusion-stage-' + $id)
$backup = Join-Path $parent ('.AutoBIMFusion-backup-' + $id)
# Every rename and recursive cleanup below is confined to these checked siblings.
foreach ($path in @($target, $stage, $backup)) {
    if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($path)) -ne $parent) {
        throw "Publication path escapes target directory: $path"
    }
}
$lock = [IO.File]::Open((Join-Path $parent '.AutoBIMFusion-install.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
$published = $false
try {
    Copy-Item -LiteralPath $source -Destination $stage -Recurse
    [xml]$manifest = Get-Content -LiteralPath (Join-Path $stage 'PackageContents.xml') -Raw
    if ($manifest.ApplicationPackage.Components.ComponentEntry.ModuleName -ne './Contents/AutoBIMFusion.dll') {
        throw 'Invalid bundle entry point.'
    }
    foreach ($name in @('AutoBIMFusion', 'AutoBIMFusion.Common', 'AutoBIMFusion.Merge', 'Serilog', 'Serilog.Sinks.File')) {
        if (-not (Test-Path -LiteralPath (Join-Path $stage "Contents\$name.dll") -PathType Leaf)) {
            throw "Incomplete bundle: $name.dll is missing."
        }
    }
    $hash = [Security.Cryptography.SHA256]::Create()
    try {
        foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse) {
            $relative = $file.FullName.Substring($source.Length + 1)
            $originalHash = [Convert]::ToBase64String($hash.ComputeHash([IO.File]::ReadAllBytes($file.FullName)))
            $copiedHash = [Convert]::ToBase64String($hash.ComputeHash([IO.File]::ReadAllBytes((Join-Path $stage $relative))))
            if ($originalHash -ne $copiedHash) { throw "Bundle copy verification failed: $relative" }
        }
    }
    finally { $hash.Dispose() }
    if (Test-Path -LiteralPath $target) { [IO.Directory]::Move($target, $backup) }
    try {
        [IO.Directory]::Move($stage, $target)
        $published = $true
    }
    catch {
        if (Test-Path -LiteralPath $backup) { [IO.Directory]::Move($backup, $target) }
        throw
    }
}
finally {
    try {
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
        if ($published -and (Test-Path -LiteralPath $backup)) {
            # Loaded DLLs may keep the old bundle locked. Preserve it until AutoCAD exits.
            try { Remove-Item -LiteralPath $backup -Recurse -Force }
            catch { Write-Warning "Published successfully; old files remain at $backup. Restart AutoCAD before removing them." }
        }
    }
    finally { $lock.Dispose() }
}
Write-Host "AutoCAD bundle published: $target"
