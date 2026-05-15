param(
    [string]$Configuration = "DebugA26"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$targetFramework = if ($Configuration.EndsWith("A27", [System.StringComparison]::OrdinalIgnoreCase)) { "net10.0-windows" } else { "net8.0-windows" }
$sourceBundle = Join-Path $repoRoot "src\AutoBIMFusion.Plugin\bin\x64\$Configuration\$targetFramework\AutoBIMFusion.bundle"
$targetBundle = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\AutoBIMFusion.bundle"

function Clear-FileAttributes {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    Get-ChildItem -LiteralPath $Path -Recurse -Force |
        ForEach-Object {
            $_.Attributes = [System.IO.FileAttributes]::Normal
        }

    $item = Get-Item -LiteralPath $Path -Force
    if ($item.PSIsContainer) {
        $item.Attributes = [System.IO.FileAttributes]::Directory
    }
    else {
        $item.Attributes = [System.IO.FileAttributes]::Normal
    }
}

if (-not (Test-Path -LiteralPath $sourceBundle -PathType Container)) {
    Push-Location $repoRoot
    try {
        & dotnet build "AutoBIMFusion.slnx" -c $Configuration "-p:AutoCADUserPluginsDir=$env:TEMP\AutoBIMFusion-InstallBuild\"

        if ($LASTEXITCODE -ne 0) {
            throw "Build failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

if (-not (Test-Path -LiteralPath $sourceBundle -PathType Container)) {
    throw "Bundle was not found after build: $sourceBundle"
}

if (Test-Path -LiteralPath $targetBundle) {
    Clear-FileAttributes -Path $targetBundle
    Remove-Item -LiteralPath $targetBundle -Recurse -Force
}

New-Item -ItemType Directory -Path (Split-Path $targetBundle -Parent) -Force | Out-Null
Copy-Item -LiteralPath $sourceBundle -Destination $targetBundle -Recurse -Force

Write-Host "AutoBIMFusion bundle installed:"
Write-Host $targetBundle
Write-Host "Restart AutoCAD and run MERGEDWG."
