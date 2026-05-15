param(
    [string]$AutoCADRoot = "C:\Program Files\Autodesk\AutoCAD 2026",
    [string]$Configuration = "DebugA26",
    [int]$MaxParallel = 2,
    [int]$StartDelaySeconds = 10,
    [int]$TimeoutMinutes = 240,
    [switch]$SkipBuild,
    [switch]$WhatIf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Test-ContainsDwg {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FolderPath
    )

    $dwg = Get-ChildItem -LiteralPath $FolderPath -Filter "*.dwg" -File -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1

    return $null -ne $dwg
}

function Complete-FinishedProcesses {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.ArrayList]$ActiveProcesses,

        [Parameter(Mandatory = $true)]
        [System.Collections.ArrayList]$CompletedProcesses,

        [Parameter(Mandatory = $true)]
        [int]$TimeoutMinutes
    )

    $now = Get-Date

    foreach ($item in @($ActiveProcesses)) {
        $timedOut = ($now - $item.StartedAt).TotalMinutes -ge $TimeoutMinutes

        if ($timedOut -and -not $item.Process.HasExited) {
            Stop-Process -Id $item.Process.Id -Force -ErrorAction SilentlyContinue
            $item.TimedOut = $true
        }

        if ($item.Process.HasExited -or $item.TimedOut) {
            [void]$ActiveProcesses.Remove($item)
            [void]$CompletedProcesses.Add($item)
        }
    }
}

function New-AutoCadScript {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ScriptPath,

        [Parameter(Mandatory = $true)]
        [string]$PluginPath,

        [Parameter(Mandatory = $true)]
        [string]$FolderPath,

        [Parameter(Mandatory = $true)]
        [string]$StatusPath
    )

    $lines = @(
        "FILEDIA",
        "0",
        "CMDDIA",
        "0",
        "SECURELOAD",
        "0",
        "NETLOAD",
        $PluginPath,
        "MERGEDWG_BATCH",
        $FolderPath,
        $StatusPath,
        "._QUIT",
        "_Y"
    )

    Set-Content -LiteralPath $ScriptPath -Value $lines -Encoding Default
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$workRoot = (Get-Location).ProviderPath
$acadExe = Join-Path $AutoCADRoot "acad.exe"
$outputSuffix = -join ([int[]](45, 1089, 1073, 1086, 1088, 1082, 1072) | ForEach-Object { [char]$_ })
$successMessage = -join ([int[]](1042, 1089, 1077, 32, 1087, 1072, 1087, 1082, 1080, 32, 1091, 1089, 1087, 1077, 1096, 1085, 1086, 32, 1086, 1073, 1088, 1072, 1073, 1086, 1090, 1072, 1085, 1099, 46) | ForEach-Object { [char]$_ })

if ($MaxParallel -lt 1) {
    throw "MaxParallel must be greater than 0."
}

if ($StartDelaySeconds -lt 0) {
    throw "StartDelaySeconds cannot be negative."
}

if ($TimeoutMinutes -lt 1) {
    throw "TimeoutMinutes must be greater than 0."
}

if (-not (Test-Path -LiteralPath $acadExe -PathType Leaf)) {
    throw "AutoCAD was not found: $acadExe"
}

$folders = @(Get-ChildItem -LiteralPath $workRoot -Directory |
    Where-Object { -not $_.Name.EndsWith($outputSuffix, [System.StringComparison]::OrdinalIgnoreCase) } |
    Where-Object { Test-ContainsDwg -FolderPath $_.FullName } |
    Sort-Object Name)

if ($folders.Count -eq 0) {
    Write-Host "No folders with DWG files found: $workRoot"
    exit 0
}

$runStamp = Get-Date -Format "yyyyMMdd-HHmmss"
$runRoot = Join-Path ([System.IO.Path]::GetTempPath()) "AutoBIMFusion-MERGEDWG-$runStamp"
$statusRoot = Join-Path $runRoot "status"
$scriptTempRoot = Join-Path $runRoot "scripts"
$tempApplicationPluginsRoot = Join-Path $runRoot "ApplicationPlugins"
$targetFramework = if ($Configuration.EndsWith("A27", [System.StringComparison]::OrdinalIgnoreCase)) { "net10.0-windows" } else { "net8.0-windows" }
$pluginPath = Join-Path $repoRoot "src\AutoBIMFusion.Plugin\bin\x64\$Configuration\$targetFramework\AutoBIMFusion.dll"

New-Item -ItemType Directory -Path $statusRoot, $scriptTempRoot, $tempApplicationPluginsRoot -Force | Out-Null

$items = New-Object System.Collections.ArrayList
$index = 0

foreach ($folder in $folders) {
    $index++
    $safeName = $folder.Name -replace '[^\p{L}\p{Nd}\._-]', '_'
    $baseName = "{0:D3}-{1}" -f $index, $safeName
    $statusPath = Join-Path $statusRoot "$baseName.json"
    $scriptPath = Join-Path $scriptTempRoot "$baseName.scr"

    New-AutoCadScript -ScriptPath $scriptPath -PluginPath $pluginPath -FolderPath $folder.FullName -StatusPath $statusPath

    [void]$items.Add([pscustomobject]@{
        FolderPath = $folder.FullName
        StatusPath = $statusPath
        ScriptPath = $scriptPath
        Process = $null
        StartedAt = $null
        TimedOut = $false
        StartError = $null
    })
}

Write-Host "Folders queued: $($items.Count)"
Write-Host "Batch workspace: $runRoot"

if ($WhatIf) {
    foreach ($item in $items) {
        Write-Host "[WhatIf] $($item.FolderPath)"
        Write-Host "         script: $($item.ScriptPath)"
        Write-Host "         status: $($item.StatusPath)"
    }

    Write-Host "Validation completed without starting AutoCAD."
    exit 0
}

if (-not $SkipBuild) {
    Push-Location $repoRoot
    try {
        & dotnet build "AutoBIMFusion.slnx" -c $Configuration "-p:AutoCADUserPluginsDir=$tempApplicationPluginsRoot\"

        if ($LASTEXITCODE -ne 0) {
            throw "Build failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

if (-not (Test-Path -LiteralPath $pluginPath -PathType Leaf)) {
    throw "Plugin DLL was not found: $pluginPath"
}

$active = New-Object System.Collections.ArrayList
$completed = New-Object System.Collections.ArrayList

foreach ($item in $items) {
    while ($active.Count -ge $MaxParallel) {
        Complete-FinishedProcesses -ActiveProcesses $active -CompletedProcesses $completed -TimeoutMinutes $TimeoutMinutes
        Start-Sleep -Seconds 2
    }

    try {
        $arguments = '/nologo /b "{0}"' -f $item.ScriptPath
        $process = Start-Process -FilePath $acadExe -ArgumentList $arguments -WindowStyle Minimized -PassThru
        $item.Process = $process
        $item.StartedAt = Get-Date
        [void]$active.Add($item)

        Write-Host "Started AutoCAD PID=$($process.Id): $($item.FolderPath)"
    }
    catch {
        $item.StartedAt = Get-Date
        $item.StartError = $_.Exception.Message
        [void]$completed.Add($item)
        Write-Host "Failed to start AutoCAD: $($item.FolderPath)"
    }

    if ($StartDelaySeconds -gt 0) {
        Start-Sleep -Seconds $StartDelaySeconds
    }

    Complete-FinishedProcesses -ActiveProcesses $active -CompletedProcesses $completed -TimeoutMinutes $TimeoutMinutes
}

while ($active.Count -gt 0) {
    Complete-FinishedProcesses -ActiveProcesses $active -CompletedProcesses $completed -TimeoutMinutes $TimeoutMinutes
    Start-Sleep -Seconds 2
}

$failures = New-Object System.Collections.ArrayList

foreach ($item in $completed) {
    $reason = $null
    $status = $null

    if (-not [string]::IsNullOrWhiteSpace($item.StartError)) {
        $reason = "AutoCAD failed to start: $($item.StartError)"
    }
    elseif ($item.TimedOut) {
        $reason = "AutoCAD was stopped after timeout: $TimeoutMinutes min."
    }
    elseif (-not (Test-Path -LiteralPath $item.StatusPath -PathType Leaf)) {
        $reason = "Status file was not created."
    }
    else {
        $status = Get-Content -LiteralPath $item.StatusPath -Raw | ConvertFrom-Json

        if (-not [bool]$status.success) {
            $reason = $status.message
        }
    }

    if ($null -ne $item.Process -and $item.Process.ExitCode -ne 0 -and [string]::IsNullOrWhiteSpace($reason)) {
        $reason = "AutoCAD exited with code $($item.Process.ExitCode)."
    }

    if (-not [string]::IsNullOrWhiteSpace($reason)) {
        [void]$failures.Add([pscustomobject]@{
            FolderPath = $item.FolderPath
            Reason = $reason
            StatusPath = $item.StatusPath
            ScriptPath = $item.ScriptPath
            LogPath = if ($null -ne $status) { $status.logPath } else { $null }
        })
    }
}

if ($failures.Count -eq 0) {
    Write-Host $successMessage
    exit 0
}

Write-Host "Some folders failed:"

foreach ($failure in $failures) {
    Write-Host "- $($failure.FolderPath)"
    Write-Host "  Reason: $($failure.Reason)"
    Write-Host "  Status: $($failure.StatusPath)"

    if (-not [string]::IsNullOrWhiteSpace($failure.LogPath)) {
        Write-Host "  Log: $($failure.LogPath)"
    }
}

exit 1
