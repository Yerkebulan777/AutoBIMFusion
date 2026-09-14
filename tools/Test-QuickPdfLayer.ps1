param(
    [Parameter(Mandatory = $true)]
    [string]$AutoCADRoot,
    [ValidatePattern('^(Debug|Release)A(19|2[0-7])$')]
    [string]$Configuration = 'DebugA26'
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

$runRoot = Join-Path $repoRoot ("out\compatibility\quickpdf-layer-" + $Configuration + '-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

$project = Join-Path $repoRoot 'src\AutoBIMFusion.Plugin\AutoBIMFusion.Plugin.csproj'
$properties = @(
    "-p:Configuration=$Configuration",
    '-p:Platform=x64',
    '-p:CoreConsoleDiagnostics=true',
    "-p:AutoCADUserPluginsDir=$runRoot\deploy\"
)
& dotnet build $project @properties -v:q
if ($LASTEXITCODE -ne 0) { throw 'QuickPDF layer test build failed.' }

$targetDir = (& dotnet msbuild $project @properties '-getProperty:TargetDir' | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot evaluate plugin path.' }
$pluginPath = (Join-Path $targetDir 'AutoBIMFusion.bundle\Contents\AutoBIMFusion.dll').Replace('\', '/')
$resultPath = (Join-Path $runRoot 'result.txt').Replace('\', '/')
$drawingPath = (Join-Path $runRoot 'quickpdf-layer.dwg').Replace('\', '/')
$scriptPath = Join-Path $runRoot 'quickpdf-layer.scr'

$lines = @(
    '(setvar "FILEDIA" 0)',
    '(setvar "SECURELOAD" 0)',
    ('(command "_.QSAVE" "' + $drawingPath + '")'),
    '_.NETLOAD',
    $pluginPath,
    'QUICKPDF',
    '0,0',
    '0,0',
    '(setq firstLayer (entget (tblobjname "LAYER" "FRAMELIST")))',
    '._-LAYER',
    '_Color',
    '1',
    'FRAMELIST',
    '',
    'QUICKPDF',
    '0,0',
    '0,0',
    '(setq secondLayer (entget (tblobjname "LAYER" "FRAMELIST")))',
    ('(setq output (open "' + $resultPath + '" "w"))'),
    '(if firstLayer (write-line (strcat (cdr (assoc 2 firstLayer)) "|" (itoa (cdr (assoc 62 firstLayer))) "|" (itoa (cdr (assoc 370 firstLayer))) "|" (itoa (cdr (assoc 290 firstLayer))) "|" (cdr (assoc 6 firstLayer))) output) (write-line "MISSING" output))',
    '(if secondLayer (write-line (itoa (cdr (assoc 62 secondLayer))) output) (write-line "MISSING" output))',
    '(close output)',
    '._QUIT',
    '_Y',
    ''
)

$encoding = [Text.Encoding]::GetEncoding(
    [Globalization.CultureInfo]::CurrentCulture.TextInfo.ANSICodePage,
    [Text.EncoderFallback]::ExceptionFallback,
    [Text.DecoderFallback]::ExceptionFallback)
[IO.File]::WriteAllLines($scriptPath, $lines, $encoding)

$arguments = @(
    '/s', ('"' + $scriptPath + '"'),
    '/isolate', (Split-Path $runRoot -Leaf), ('"' + (Join-Path $runRoot 'profile') + '"')
)
$process = Start-Process -FilePath $hostExe -ArgumentList $arguments -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $runRoot 'console.log') `
    -RedirectStandardError (Join-Path $runRoot 'console-error.log')
if (-not $process.WaitForExit(120000)) {
    $process.Kill()
    $process.WaitForExit()
    throw "Core Console timed out. See $runRoot"
}
if ($process.ExitCode -ne 0) { throw "Core Console failed with exit $($process.ExitCode). See $runRoot" }
if (-not (Test-Path -LiteralPath $resultPath)) { throw "QuickPDF layer result was not written. See $runRoot" }

$result = @(Get-Content -LiteralPath $resultPath)
if ($result.Count -ne 2) { throw "Unexpected QuickPDF layer result. See $runRoot" }
if ($result[0].ToUpperInvariant() -ne 'FRAMELIST|4|50|0|CONTINUOUS') {
    throw "Unexpected initial FRAMELIST settings: $($result[0]). See $runRoot"
}
if ($result[1] -ne '1') {
    throw "Existing FRAMELIST layer was modified: color=$($result[1]). See $runRoot"
}

Write-Host "PASS ${Configuration}: FRAMELIST is created once with cyan color, 0.50 mm lineweight and plotting disabled. Artifacts: $runRoot"
