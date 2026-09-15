function New-InstallerTestBundle {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][int]$Year
    )

    $series = switch ($Year) {
        2019 { 'R23.0' } 2020 { 'R23.1' } 2021 { 'R24.0' }
        2022 { 'R24.1' } 2023 { 'R24.2' } 2024 { 'R24.3' }
        2025 { 'R25.0' } 2026 { 'R25.1' } 2027 { 'R26.0' }
        default { throw "Unsupported AutoCAD year: $Year" }
    }
    $bundle = Join-Path $Root "source\$Year\AutoBIMFusion.bundle"
    $contents = Join-Path $bundle 'Contents\Resources'
    New-Item -ItemType Directory -Path $contents -Force | Out-Null
    $assembly = Join-Path $Root 'InstallerFixture.dll'
    if (-not (Test-Path -LiteralPath $assembly)) {
        Add-Type -TypeDefinition 'public class InstallerFixture {}' -OutputAssembly $assembly
    }
    foreach ($name in $AutoCADPluginRequiredDlls) {
        Copy-Item -LiteralPath $assembly -Destination (Join-Path $bundle "Contents\$name")
    }
    'icon' | Set-Content -LiteralPath (Join-Path $contents 'icon-merge-dwg-32.png')
    # Shape matches the plugin-generated single-year PackageContents.xml.
    @"
<ApplicationPackage SchemaVersion="1.0" ProductCode="{4F3E42D4-4D2F-4A2E-8A2A-BB771A0D06AF}" Name="AutoBIMFusion">
  <CompanyDetails Name="AutoBIMFusion" />
  <Components>
    <RuntimeRequirements OS="Win64" Platform="AutoCAD" SeriesMin="$series" SeriesMax="$series" />
    <ComponentEntry AppName="AutoBIMFusion" ModuleName="./Contents/AutoBIMFusion.dll" AppType=".Net" LoadOnAutoCADStartup="true">
      <RuntimeRequirements OS="Win64" Platform="AutoCAD" SeriesMin="$series" SeriesMax="$series" />
    </ComponentEntry>
  </Components>
</ApplicationPackage>
"@ | Set-Content -LiteralPath (Join-Path $bundle 'PackageContents.xml')
    $bundle
}


