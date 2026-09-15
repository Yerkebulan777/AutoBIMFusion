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
    try {
        [void][Reflection.AssemblyName]::GetAssemblyName($Path)
        return $true
    }
    catch { return $false }
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
    }
    $hostDlls = @(Get-ChildItem -LiteralPath $ContentsDir -Filter '*.dll' -File |
        Where-Object Name -Match '^(acmgd|acdbmgd|accoremgd|AdWindows|AcWindows|Autodesk\.|Aecc|AecBase)')
    if ($hostDlls.Count -gt 0) {
        throw "Host DLLs in ${Context}: $($hostDlls.Name -join ', ')"
    }
    if ($RequireAssemblies) {
        foreach ($dll in Get-ChildItem -LiteralPath $ContentsDir -Filter '*.dll' -File -Recurse) {
            if (-not (Test-AutoCADPluginAssembly $dll.FullName)) {
                throw "Invalid managed DLL in ${Context}: $($dll.FullName)"
            }
        }
    }
}
