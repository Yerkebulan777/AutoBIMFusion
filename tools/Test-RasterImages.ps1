param(
    [Parameter(Mandatory = $true)][string]$AutoCADRoot,
    [string]$Configuration = 'DebugA19'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$hostExe = Join-Path $AutoCADRoot 'accoreconsole.exe'
$settings = & (Join-Path $PSScriptRoot 'Get-AutoCADBuildSettings.ps1') -Configuration $Configuration
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($hostExe)
if ("R$($version.FileMajorPart).$($version.FileMinorPart)" -ne $settings.AutoCADSeries) {
    throw 'Configuration and host version must match.'
}
$project = Join-Path $repo 'tests\AutoBIMFusion.Raster.HostTests\AutoBIMFusion.Raster.HostTests.csproj'
$properties = @("-p:Configuration=$Configuration", '-p:CoreConsoleDiagnostics=true')
& dotnet build $project @properties -v:q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$dll = (& dotnet msbuild $project @properties '-getProperty:TargetPath' | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot evaluate test assembly path.' }
$runRoot = Join-Path $repo ('out\raster-tests\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$script = Join-Path $runRoot 'test.scr'
[IO.File]::WriteAllLines($script, @('FILEDIA', '0', 'SECURELOAD', '0', 'NETLOAD', $dll,
    'ABF_RASTER_TEST', '._QUIT', '_Y', ''), [Text.Encoding]::ASCII)
$previousRoot = $env:ABF_RASTER_TEST_ROOT
try {
    $env:ABF_RASTER_TEST_ROOT = $runRoot
    $process = Start-Process -FilePath $hostExe -WindowStyle Hidden -PassThru -ArgumentList @(
        '/s', ('"' + $script + '"'), '/isolate', (Split-Path $runRoot -Leaf),
        ('"' + (Join-Path $runRoot 'profile') + '"')) `
        -RedirectStandardOutput (Join-Path $runRoot 'host.log') -RedirectStandardError (Join-Path $runRoot 'host-error.log')
    if (-not $process.WaitForExit(120000)) {
        $process.Kill()
        throw "Host test timed out: $runRoot"
    }
    $result = Get-Content -LiteralPath (Join-Path $runRoot 'result.txt')
    $result
    if ($process.ExitCode -ne 0 -or $result -match '^FAIL') { throw "Raster regression failed: $runRoot" }
    Write-Host "PASS raster host regression: $runRoot"
}
finally { $env:ABF_RASTER_TEST_ROOT = $previousRoot }
