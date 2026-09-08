param(
    [Parameter(Mandatory = $true)]
    [string]$AutoCADRoot,
    [ValidatePattern('^(Debug|Release)A(19|2[0-7])$')]
    [string]$Configuration = 'DebugA19'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$hostExe = Join-Path $AutoCADRoot 'accoreconsole.exe'
if (-not (Test-Path -LiteralPath $hostExe)) { throw "Core Console not found: $hostExe" }
$settings = & (Join-Path $PSScriptRoot 'Get-AutoCADBuildSettings.ps1') -Configuration $Configuration
$hostVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($hostExe)
if ("R$($hostVersion.FileMajorPart).$($hostVersion.FileMinorPart)" -ne $settings.AutoCADSeries) {
    throw "Host version does not match $Configuration ($($settings.AutoCADSeries))."
}
$runRoot = Join-Path $repoRoot ("out\compatibility\host-" + $Configuration + '-' + [Guid]::NewGuid().ToString('N'))
$inputRoot = Join-Path $runRoot 'input'
New-Item -ItemType Directory -Path $inputRoot -Force | Out-Null

function Invoke-CoreScript {
    param([string]$Name, [string[]]$Lines, [string]$Drawing)
    $scriptPath = Join-Path $runRoot "$Name.scr"
    # Older AutoCAD reads scripts in the Windows ANSI code page. Fail rather than corrupt paths.
    $encoding = [Text.Encoding]::GetEncoding([Globalization.CultureInfo]::CurrentCulture.TextInfo.ANSICodePage,
        [Text.EncoderFallback]::ExceptionFallback, [Text.DecoderFallback]::ExceptionFallback)
    [IO.File]::WriteAllLines($scriptPath, $Lines, $encoding)
    $arguments = @('/s', ('"' + $scriptPath + '"'), '/isolate',
        (Split-Path $runRoot -Leaf), ('"' + (Join-Path $runRoot 'profile') + '"'))
    if ($Drawing) { $arguments += @('/i', ('"' + $Drawing + '"')) }
    $process = Start-Process -FilePath $hostExe -ArgumentList $arguments -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $runRoot "$Name.log") -RedirectStandardError (Join-Path $runRoot "$Name-error.log")
    if (-not $process.WaitForExit(120000)) {
        $process.Kill()
        $process.WaitForExit()
        throw "Core Console timed out: $Name. See $runRoot"
    }
    if ($process.ExitCode -ne 0) { throw "Core Console failed: $Name, exit $($process.ExitCode). See $runRoot" }
}

$project = Join-Path $repoRoot 'src\AutoBIMFusion.Plugin\AutoBIMFusion.Plugin.csproj'
$properties = @("-p:Configuration=$Configuration", '-p:Platform=x64', '-p:CoreConsoleDiagnostics=true', "-p:AutoCADUserPluginsDir=$runRoot\deploy\")
& dotnet build $project @properties -v:q
if ($LASTEXITCODE -ne 0) { throw 'Host test build failed.' }
$targetDir = (& dotnet msbuild $project @properties '-getProperty:TargetDir' | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot evaluate plugin path.' }
$pluginPath = Join-Path $targetDir 'AutoBIMFusion.bundle\Contents\AutoBIMFusion.dll'

$fixture = (Join-Path $inputRoot 'fixture.dwg').Replace('\', '/')
Invoke-CoreScript -Name 'fixture' -Lines @(
    '(setvar "FILEDIA" 0)',
    '(command "_.RECTANG" "_non" (list 0 0) "_non" (list 100 100))',
    '(foreach tab (layoutlist) (setvar "CTAB" tab) (command "_.PSPACE") (command "_.RECTANG" "_non" (list 0 0) "_non" (list 297 210)) (command "_.MVIEW" "_non" (list 10 10) "_non" (list 280 190)) (command "_.MSPACE") (command "_.ZOOM" "_E") (command "_.PSPACE"))',
    ('(command "_.QSAVE" "' + $fixture + '")'), '._QUIT', '_Y', '')
if (-not (Test-Path -LiteralPath $fixture)) { throw 'Fixture DWG was not created.' }

$statusPath = Join-Path $runRoot 'status.json'
Invoke-CoreScript -Name 'merge' -Lines @('FILEDIA', '0', 'SECURELOAD', '0', 'NETLOAD', $pluginPath,
    'MERGEDWG_BATCH', $inputRoot, $statusPath, '._QUIT', '_Y', '')
$status = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json
if (-not $status.success -or -not (Test-Path -LiteralPath $status.savePath)) {
    throw "Merge failed: $($status.message)"
}
$countPath = (Join-Path $runRoot 'count.txt').Replace('\', '/')
Invoke-CoreScript -Name 'verify' -Drawing $status.savePath -Lines @(
    '(setq selected (ssget "_X" (list (cons 67 0))))',
    ('(setq output (open "' + $countPath + '" "w"))'),
    '(write-line (itoa (if selected (sslength selected) 0)) output)', '(close output)', '._QUIT', '_Y', '')
$count = [int](Get-Content -LiteralPath $countPath -Raw)
if ($count -lt 1) { throw 'Merged drawing is empty; expected exported layout geometry.' }
if (-not (Test-Path -LiteralPath $status.logPath)) { throw 'Serilog file was not created.' }
# The input folder contains a fresh GUID: an earlier daily log cannot satisfy this check.
if (-not (Select-String -LiteralPath $status.logPath -Pattern $inputRoot -SimpleMatch -Quiet -Encoding UTF8)) {
    throw 'Serilog did not write a record for this test run.'
}
Write-Host "PASS ${Configuration}: $count model entities, saved DWG and Serilog log. Artifacts: $runRoot"
