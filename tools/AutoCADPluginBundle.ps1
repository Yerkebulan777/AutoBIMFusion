Set-StrictMode -Version Latest

$AutoCADPluginRequiredDlls = @(
    'AutoBIMFusion.dll',
    'AutoBIMFusion.Common.dll',
    'AutoBIMFusion.Merge.dll',
    'AutoBIMFusion.QuickPdf.dll',
    'Serilog.dll',
    'Serilog.Sinks.File.dll'
)

function Test-AutoCADPluginAssembly {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    $stream = [IO.File]::OpenRead($Path)
    try {
        $mz = $stream.ReadByte()
        $z = $stream.ReadByte()
        $mz -eq 0x4D -and $z -eq 0x5A
    }
    finally { $stream.Dispose() }
}

function Assert-AutoCADPluginContents {
    param(
        [Parameter(Mandatory = $true)][string]$ContentsDir,
        [Parameter(Mandatory = $true)][string]$Context,
        [switch]$RequireAssemblies
    )

    foreach ($name in $AutoCADPluginRequiredDlls) {
        $path = Join-Path $ContentsDir $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Incomplete ${Context}: $name is missing in $ContentsDir"
        }
        if ($RequireAssemblies -and -not (Test-AutoCADPluginAssembly $path)) {
            throw "Not a real plugin assembly in ${Context}: $name"
        }
    }
    $hostDlls = @(Get-ChildItem -LiteralPath $ContentsDir -Filter '*.dll' -File |
        Where-Object Name -Match '^(acmgd|acdbmgd|accoremgd|AdWindows|AcWindows|Autodesk\.|Aecc|AecBase)')
    if ($hostDlls.Count -gt 0) {
        throw "Host DLLs in ${Context}: $($hostDlls.Name -join ', ')"
    }
}
