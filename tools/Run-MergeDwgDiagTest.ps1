param(
    [string]$Configuration = "DebugA26",
    [string]$AutoCADRoot = "C:\Program Files\Autodesk\AutoCAD 2026",
    [int]$TimeoutSeconds = 600,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot "AutoBIMFusion.slnx"
$coreConsole = Join-Path $AutoCADRoot "accoreconsole.exe"
$coreOutputRoot = Join-Path $repoRoot "AutoBIMFusion\bin\$Configuration-core"
$bundleContents = Join-Path $coreOutputRoot "AutoBIMFusion.bundle\Contents"
$dllPath = Join-Path $bundleContents "AutoBIMFusion.dll"
$workDir = Join-Path $coreOutputRoot "diag"
$scriptPath = Join-Path $workDir "mergedwg-diag-test.scr"
$stdoutPath = Join-Path $workDir "accoreconsole.out.log"
$stderrPath = Join-Path $workDir "accoreconsole.err.log"

if (-not (Test-Path $coreConsole)) {
    throw "AutoCAD Core Console not found: $coreConsole"
}

if (-not $SkipBuild) {
    dotnet build $solutionPath -c $Configuration `
        /p:CoreConsoleDiagnostics=true `
        /p:OutputPath="$coreOutputRoot\" `
        /p:AutoCADUserPluginsDir="$coreOutputRoot\LocalApplicationPlugins\"
}

if (-not (Test-Path $dllPath)) {
    throw "Plugin DLL not found: $dllPath"
}

New-Item -ItemType Directory -Path $workDir -Force | Out-Null

$escapedDllPath = $dllPath.Replace("\", "/")
$script = @"
FILEDIA
0
CMDDIA
0
SECURELOAD
0
(command "_.NETLOAD" "$escapedDllPath")
MERGEDWG_DIAG_TEST
(command "_.QUIT" "_Y")
"@

Set-Content -Path $scriptPath -Value $script -Encoding ASCII
Remove-Item -Path $stdoutPath, $stderrPath -ErrorAction SilentlyContinue

$process = Start-Process `
    -FilePath $coreConsole `
    -ArgumentList @("/s", $scriptPath) `
    -WorkingDirectory $workDir `
    -RedirectStandardOutput $stdoutPath `
    -RedirectStandardError $stderrPath `
    -PassThru `
    -WindowStyle Hidden

if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    try {
        Stop-Process -Id $process.Id -Force
    }
    catch {
        Write-Warning "Failed to stop timed-out accoreconsole process $($process.Id): $_"
    }

    throw "accoreconsole timed out after $TimeoutSeconds seconds. stdout=$stdoutPath stderr=$stderrPath"
}

Write-Host "accoreconsole exit code: $($process.ExitCode)"
Write-Host "stdout: $stdoutPath"
Write-Host "stderr: $stderrPath"
