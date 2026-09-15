Set-StrictMode -Version Latest

$AutoCADPluginRequiredDlls = @(
    'AutoBIMFusion.dll',
    'AutoBIMFusion.Common.dll',
    'AutoBIMFusion.Merge.dll',
    'AutoBIMFusion.QuickPdf.dll',
    'Serilog.dll',
    'Serilog.Sinks.File.dll'
)

function Assert-AutoCADPluginContents {
    param(
        [Parameter(Mandatory = $true)][string]$ContentsDir,
        [Parameter(Mandatory = $true)][string]$Context
    )

    foreach ($name in $AutoCADPluginRequiredDlls) {
        if (-not (Test-Path -LiteralPath (Join-Path $ContentsDir $name) -PathType Leaf)) {
            throw "Incomplete ${Context}: $name is missing in $ContentsDir"
        }
    }
    $hostDlls = @(Get-ChildItem -LiteralPath $ContentsDir -Filter '*.dll' -File |
        Where-Object Name -Match '^(acmgd|acdbmgd|accoremgd|AdWindows|AcWindows|Autodesk\.|Aecc|AecBase)')
    if ($hostDlls.Count -gt 0) {
        throw "Host DLLs in ${Context}: $($hostDlls.Name -join ', ')"
    }
}
