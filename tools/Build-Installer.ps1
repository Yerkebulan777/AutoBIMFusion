param(
    [Parameter(Mandatory = $true)][ValidateRange(2019, 2027)][int]$Year,
    [ValidateSet('Debug', 'Release')]
    [string]$BuildType = 'Release',
    [string]$Version = '1.0.0',
    [string]$SignThumbprint = $env:AUTOBIMFUSION_SIGN_THUMBPRINT,
    [string]$SignPfx = $env:AUTOBIMFUSION_SIGN_PFX,
    [string]$SignPfxPassword = $env:AUTOBIMFUSION_SIGN_PFX_PASSWORD,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [switch]$SkipSign
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
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

$wixProject = Join-Path $repoRoot 'installer\AutoBIMFusion.Installer.wixproj'
$configuration = "$BuildType`A$($Year - 2000)"
$payload = Join-Path $repoRoot "out\installer\payload\$configuration\AutoBIMFusion.bundle"
$releaseDir = Join-Path $repoRoot 'out\installer'
if ($BuildType -eq 'Debug') { $releaseDir = Join-Path $releaseDir 'Debug' }
$releaseMsi = Join-Path $releaseDir "AutoBIMFusion-AutoCAD$Year-$Version.msi"
if (-not (Test-Path -LiteralPath $payload -PathType Container) -or
    -not (Test-Path -LiteralPath $releaseMsi -PathType Leaf)) {
    throw "Build the installer first: dotnet build AutoBIMFusion.slnx -c $configuration"
}

$payloadDlls = @(Get-ChildItem -LiteralPath $payload -Recurse -Filter '*.dll' -File |
    Select-Object -ExpandProperty FullName)
$signedPayload = $false
if ($payloadDlls.Count -gt 0) {
    $signedPayload = Invoke-AuthenticodeSign -Files $payloadDlls
    if ($signedPayload) {
        Write-Host "Signed $($payloadDlls.Count) payload DLLs; rebuilding MSI to embed signatures."
        & dotnet build $wixProject -c $configuration -p:Platform=x64 "-p:Version=$Version" `
            '-p:SkipInstallerDependencyBuild=true'
        if ($LASTEXITCODE -ne 0) { throw 'Installer rebuild after signing failed.' }
    }
}

if (Invoke-AuthenticodeSign -Files @($releaseMsi)) {
    Write-Host "Signed MSI: $releaseMsi"
}
elseif (-not $SkipSign -and -not $signedPayload) {
    Write-Warning 'Nothing signed. Pass -SignThumbprint or AUTOBIMFUSION_SIGN_THUMBPRINT.'
}

Write-Host "MSI ready: $releaseMsi"
Write-Host "Installs to: $env:ProgramFiles\Autodesk\ApplicationPlugins\AutoBIMFusion-$Year.bundle"
