param(
    [ValidateSet('Debug', 'Release')]
    [string]$BuildType = 'Release',
    [ValidateRange(2019, 2027)]
    [int[]]$Years = @(2019..2027),
    [string]$Version,
    [string]$SignThumbprint = $env:AUTOBIMFUSION_SIGN_THUMBPRINT,
    [string]$SignPfx = $env:AUTOBIMFUSION_SIGN_PFX,
    [string]$SignPfxPassword = $env:AUTOBIMFUSION_SIGN_PFX_PASSWORD,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [switch]$SkipBuild,
    [switch]$SkipSign
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'New-InstallerPayload.ps1')
$script:SignToolPath = $null

function Get-SignToolPath {
    if ($script:SignToolPath) { return $script:SignToolPath }
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) {
        $script:SignToolPath = $cmd.Source
        return $script:SignToolPath
    }
    $kitRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path -LiteralPath $kitRoot) {
        $found = Get-ChildItem -LiteralPath $kitRoot -Directory |
            ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
            Sort-Object -Descending |
            Select-Object -First 1
        if ($found) {
            $script:SignToolPath = $found
            return $script:SignToolPath
        }
    }
    throw 'signtool.exe not found. Install the Windows SDK or add signtool to PATH.'
}

function Invoke-AuthenticodeSign {
    param([Parameter(Mandatory = $true)][string[]]$Files)

    if ($SkipSign -or (-not $SignThumbprint -and -not $SignPfx)) { return $false }
    $signtool = Get-SignToolPath
    $signArgs = @(
        'sign', '/fd', 'SHA256', '/td', 'SHA256', '/tr', $TimestampUrl,
        '/d', 'AutoBIMFusion',
        '/du', 'https://github.com/Yerkebulan777/AutoBIMFusion'
    )
    if ($SignThumbprint) {
        $signArgs += @('/sha1', $SignThumbprint)
    }
    else {
        $signArgs += @('/f', $SignPfx)
        if ($SignPfxPassword) { $signArgs += @('/p', $SignPfxPassword) }
    }
    & $signtool @signArgs @Files
    if ($LASTEXITCODE -ne 0) { throw "Authenticode signing failed." }
    $true
}

$yearBundles = @{}
foreach ($year in $Years) {
    $configuration = "$BuildType`A$($year - 2000)"
    if (-not $SkipBuild) {
        Write-Host "Building $configuration"
        & dotnet build (Join-Path $repoRoot 'AutoBIMFusion.slnx') -c $configuration `
            '-p:CoreConsoleDiagnostics=false' '-p:DisableAutoCADDeployment=true' `
            '-p:RunAnalyzersDuringBuild=false' '-p:EnforceCodeStyleInBuild=false'
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $configuration" }
    }
    $settings = & (Join-Path $PSScriptRoot 'Get-AutoCADBuildSettings.ps1') -Configuration $configuration
    if (-not $Version) { $Version = $settings.Version }
    $bundle = Join-Path $settings.TargetDir 'AutoBIMFusion.bundle'
    if (-not (Test-Path -LiteralPath (Join-Path $bundle 'PackageContents.xml') -PathType Leaf)) {
        throw "Missing bundle for $configuration. Build it or omit -SkipBuild. Expected: $bundle"
    }
    $yearBundles[$year] = $bundle
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "MSI ProductVersion must be major.minor.build (got '$Version')."
}

$payload = Join-Path $repoRoot 'out\installer\payload\AutoBIMFusion.bundle'
Write-Host "Staging multi-version bundle: $payload"
[void](New-InstallerPayload -Destination $payload -Version $Version -YearBundles $yearBundles)

$payloadDlls = @(Get-ChildItem -LiteralPath $payload -Recurse -Filter '*.dll' -File | Select-Object -ExpandProperty FullName)
$signedPayload = Invoke-AuthenticodeSign -Files $payloadDlls
if ($signedPayload) { Write-Host "Signed $($payloadDlls.Count) payload DLLs." }
elseif (-not $SkipSign) { Write-Warning 'MSI will be unsigned. Pass -SignThumbprint or AUTOBIMFUSION_SIGN_THUMBPRINT for a signed package.' }

$wixProject = Join-Path $repoRoot 'installer\AutoBIMFusion.Installer.wixproj'
Write-Host "Building WiX4 MSI ($BuildType)"
& dotnet build $wixProject -c $BuildType -p:Platform=x64 "-p:Version=$Version" "-p:PayloadDir=$payload"
if ($LASTEXITCODE -ne 0) { throw 'WiX MSI build failed.' }

$msiRoot = Join-Path $repoRoot "out\installer\$BuildType"
$builtMsi = Get-ChildItem -LiteralPath $msiRoot -Recurse -Filter 'AutoBIMFusion.msi' -File -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $builtMsi) { throw "MSI was not produced under $msiRoot" }
$releaseMsi = Join-Path $repoRoot "out\installer\AutoBIMFusion-$Version.msi"
Copy-Item -LiteralPath $builtMsi -Destination $releaseMsi -Force
if (Invoke-AuthenticodeSign -Files @($releaseMsi)) { Write-Host "Signed MSI: $releaseMsi" }
Write-Host "MSI ready: $releaseMsi"
Write-Host 'Installs to: C:\Program Files\Autodesk\ApplicationPlugins\AutoBIMFusion.bundle'
