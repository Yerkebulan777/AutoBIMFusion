Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'New-InstallerPayload.ps1')
. (Join-Path $PSScriptRoot 'TestFixtures\InstallerBundle.ps1')

$repoRoot = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $repoRoot ('out\installer-validation-' + [Guid]::NewGuid().ToString('N'))
$source = New-InstallerTestBundle -Root $testRoot -Year 2020
$payload = Join-Path $testRoot 'AutoBIMFusion.bundle'
[void](New-InstallerPayload -SourceBundle $source -Destination $payload -Version '1.0.0' -Year 2020)
$assembly = Join-Path $testRoot 'InstallerFixture.dll'
foreach ($name in $AutoCADPluginRequiredDlls) {
    'dll-2020' | Set-Content -LiteralPath (Join-Path $payload "Contents\2020\$name")
}
$project = Join-Path $repoRoot 'installer\AutoBIMFusion.Installer.wixproj'

# Exercise the actual packaging gate, including the signing/repack path that skips compilation.
$buildArgs = @('msbuild', $project, '-t:EnsurePayload', '-nologo', '-p:Configuration=ReleaseA20',
    '-p:SkipInstallerDependencyBuild=true', "-p:PayloadDir=$payload")
$output = & dotnet @buildArgs 2>&1
if ($LASTEXITCODE -eq 0) { throw 'Packaging accepted text files masquerading as DLLs.' }
if (($output -join "`n") -notlike '*Invalid managed DLL*') { throw "Unexpected failure: $output" }

# A small, real managed assembly keeps the test independent of installed AutoCAD SDKs.
foreach ($name in $AutoCADPluginRequiredDlls) {
    Copy-Item -LiteralPath $assembly -Destination (Join-Path $payload "Contents\2020\$name")
}
$output = & dotnet @buildArgs 2>&1
if ($LASTEXITCODE -ne 0) { throw "Managed payload was rejected: $output" }

# Checking only the entry DLL or its MZ prefix must not let a broken dependency through.
$dependency = Join-Path $payload 'Contents\2020\Serilog.dll'
[IO.File]::WriteAllText($dependency, 'MZ-not-a-managed-assembly')
$output = & dotnet @buildArgs 2>&1
if ($LASTEXITCODE -eq 0) { throw 'Packaging accepted a malformed dependency DLL.' }
if (($output -join "`n") -notlike '*Invalid managed DLL*') { throw "Unexpected failure: $output" }
Copy-Item -LiteralPath $assembly -Destination $dependency

[xml]$manifest = Get-Content -LiteralPath (Join-Path $payload 'PackageContents.xml') -Raw
foreach ($req in $manifest.SelectNodes('//RuntimeRequirements')) {
    $req.SetAttribute('SeriesMin', 'R25.1')
    $req.SetAttribute('SeriesMax', 'R25.1')
}
$manifest.Save((Join-Path $payload 'PackageContents.xml'))
$output = & dotnet @buildArgs 2>&1
if ($LASTEXITCODE -eq 0) { throw 'Packaging accepted AutoCAD 2026 runtime requirements in a 2020 installer.' }
foreach ($req in $manifest.SelectNodes('//RuntimeRequirements')) {
    $req.SetAttribute('SeriesMin', 'R23.1')
    $req.SetAttribute('SeriesMax', 'R23.1')
}
$manifest.Save((Join-Path $payload 'PackageContents.xml'))

[xml]$manifest = Get-Content -LiteralPath (Join-Path $payload 'PackageContents.xml') -Raw
$node = $manifest.SelectSingleNode('/ApplicationPackage/Components[ComponentEntry/@ModuleName="./Contents/2020/AutoBIMFusion.dll"]')
$node.SelectSingleNode('ComponentEntry').SetAttribute('ModuleName', './Contents/2026/AutoBIMFusion.dll')
$manifest.Save((Join-Path $payload 'PackageContents.xml'))
$output = & dotnet @buildArgs 2>&1
if ($LASTEXITCODE -eq 0) { throw 'Packaging accepted a payload without AutoCAD 2020 autoload registration.' }
if (($output -join "`n") -notlike '*Expected only AutoCAD 2020*') { throw "Unexpected failure: $output" }

# A headless solution build must not enter desktop packaging, even with an invalid payload present.
$output = & dotnet msbuild $project -t:Build -nologo '-p:Configuration=ReleaseA20' `
    '-p:CoreConsoleDiagnostics=true' '-p:SkipInstallerDependencyBuild=true' "-p:PayloadDir=$payload" `
    "-p:OutputPath=$testRoot\headless\" "-p:IntermediateOutputPath=$testRoot\headless-obj\" 2>&1
if ($LASTEXITCODE -ne 0) { throw "Headless build entered MSI packaging: $output" }
if (Test-Path -LiteralPath (Join-Path $testRoot 'headless')) { throw 'Headless build created installer output.' }

Write-Host "PASS: MSI gate rejects stub DLLs, malformed dependencies, wrong series and missing years; headless skips MSI. Artifacts: $testRoot"
