Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'AutoCADPluginBundle.ps1')

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
    foreach ($name in $AutoCADPluginRequiredDlls) {
        "dll-$Year-$name" | Set-Content -LiteralPath (Join-Path $bundle "Contents\$name")
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

function Add-InstallerYearComponents {
    param(
        [Parameter(Mandatory = $true)][xml]$Manifest,
        [Parameter(Mandatory = $true)][xml]$Source,
        [Parameter(Mandatory = $true)][int]$Year
    )

    $sourceNodes = @($Source.SelectNodes('/ApplicationPackage/Components'))
    if ($sourceNodes.Count -ne 1) {
        throw "Year $Year source must have exactly one Components element."
    }
    $imported = $Manifest.ImportNode($sourceNodes[0], $true)
    $req = $imported.SelectSingleNode('RuntimeRequirements')
    $entry = $imported.SelectSingleNode('ComponentEntry')
    $entryReq = if ($null -eq $entry) { $null } else { $entry.SelectSingleNode('RuntimeRequirements') }
    if ($null -eq $req -or -not $req.GetAttribute('SeriesMin') -or $null -eq $entry) {
        throw "Year $Year bundle is missing Components/RuntimeRequirements or ComponentEntry."
    }
    if ($entry.GetAttribute('AppType') -ne '.Net') {
        throw "Year $Year bundle ComponentEntry must set AppType=.Net."
    }
    if ($null -eq $entryReq -or $entryReq.GetAttribute('SeriesMin') -ne $req.GetAttribute('SeriesMin')) {
        throw "Year $Year bundle ComponentEntry is missing nested RuntimeRequirements."
    }
    $req.SetAttribute('Platform', 'AutoCAD*')
    $entryReq.SetAttribute('Platform', 'AutoCAD*')
    $yearPath = "./Contents/$Year"
    $req.SetAttribute('SupportPath', $yearPath)
    $entryReq.SetAttribute('SupportPath', $yearPath)
    $entry.SetAttribute('ModuleName', "./Contents/$Year/AutoBIMFusion.dll")
    [void]$Manifest.DocumentElement.AppendChild($imported)
}

function Save-InstallerManifest {
    param(
        [Parameter(Mandatory = $true)][xml]$Manifest,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Encoding = [Text.UTF8Encoding]::new($false)
    $settings.Indent = $true
    $writer = [Xml.XmlWriter]::Create($Path, $settings)
    try { $Manifest.Save($writer) }
    finally { $writer.Dispose() }
}

function New-InstallerPayload {
    param(
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][hashtable]$YearBundles
    )

    if ($YearBundles.Count -eq 0) { throw 'No AutoCAD year bundles were supplied.' }

    $destination = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
    if ([IO.Path]::GetFileName($destination) -ne 'AutoBIMFusion.bundle') {
        throw 'Payload destination must be named AutoBIMFusion.bundle.'
    }
    if (Test-Path -LiteralPath $destination) {
        Remove-Item -LiteralPath $destination -Recurse -Force
    }

    $icon = $null
    $manifest = $null
    $productCode = $null
    foreach ($year in @($YearBundles.Keys | Sort-Object)) {
        $source = [IO.Path]::GetFullPath([string]$YearBundles[$year]).TrimEnd('\')
        if ([IO.Path]::GetFileName($source) -ne 'AutoBIMFusion.bundle') {
            throw "Year $year source must be an AutoBIMFusion.bundle folder: $source"
        }
        [xml]$sourceManifest = Get-Content -LiteralPath (Join-Path $source 'PackageContents.xml') -Raw -Encoding UTF8
        $sourceContents = Join-Path $source 'Contents'
        Assert-AutoCADPluginContents -ContentsDir $sourceContents -Context "AutoCAD $year bundle"
        $yearDir = Join-Path $destination "Contents\$year"
        New-Item -ItemType Directory -Path $yearDir -Force | Out-Null
        Copy-Item -Path (Join-Path $sourceContents '*') -Destination $yearDir -Recurse -Force
        Get-ChildItem -LiteralPath $yearDir -Filter '*.pdb' -File -Recurse -ErrorAction SilentlyContinue |
            Remove-Item -Force
        $iconFile = Join-Path $yearDir 'Resources\icon-merge-dwg-32.png'
        if (-not $icon -and (Test-Path -LiteralPath $iconFile -PathType Leaf)) {
            $icon = "./Contents/$year/Resources/icon-merge-dwg-32.png"
        }
        if ($null -eq $manifest) {
            $manifest = New-Object System.Xml.XmlDocument
            $manifest.LoadXml($sourceManifest.OuterXml)
            foreach ($node in @($manifest.SelectNodes('/ApplicationPackage/Components'))) {
                [void]$node.ParentNode.RemoveChild($node)
            }
            $manifest.DocumentElement.SetAttribute('AppVersion', $Version)
            $productCode = $manifest.DocumentElement.GetAttribute('ProductCode')
        }
        Add-InstallerYearComponents -Manifest $manifest -Source $sourceManifest -Year $year
    }
    if (-not $productCode) { throw 'Source bundles are missing ProductCode.' }
    if ($icon) { $manifest.DocumentElement.SetAttribute('Icon', $icon) }

    Save-InstallerManifest -Manifest $manifest -Path (Join-Path $destination 'PackageContents.xml')
    Assert-InstallerPayload -Destination $destination -Version $Version -ProductCode $productCode
    $destination
}

function Assert-InstallerPayload {
    param(
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$ProductCode
    )

    [xml]$manifest = Get-Content -LiteralPath (Join-Path $Destination 'PackageContents.xml') -Raw -Encoding UTF8
    $package = $manifest.ApplicationPackage
    if ($package.AppVersion -ne $Version) { throw "Unexpected AppVersion: $($package.AppVersion)" }
    if ($package.ProductCode -ne $ProductCode) { throw 'Unexpected ProductCode.' }

    $components = @($package.SelectNodes('Components'))
    if ($components.Count -eq 0) { throw 'PackageContents has no Components.' }
    $seen = @{}
    foreach ($node in $components) {
        $entry = $node.SelectSingleNode('ComponentEntry')
        $req = $node.SelectSingleNode('RuntimeRequirements')
        if ($null -eq $entry -or $null -eq $req) {
            throw 'Components is missing ComponentEntry or RuntimeRequirements.'
        }
        $module = $entry.GetAttribute('ModuleName')
        if ($module -notmatch '^\./Contents/(20(?:19|2[0-7]))/AutoBIMFusion\.dll$') {
            throw "ComponentEntry ModuleName must be a year folder: $module"
        }
        $year = [int]$Matches[1]
        if ($seen.ContainsKey($year)) { throw "Duplicate Components for $year." }
        $seen[$year] = $true
        $series = $req.GetAttribute('SeriesMin')
        $entryReq = $entry.SelectSingleNode('RuntimeRequirements')
        if ($req.GetAttribute('Platform') -ne 'AutoCAD*' -or
            $req.GetAttribute('SeriesMax') -ne $series -or
            $req.GetAttribute('SupportPath') -ne "./Contents/$year" -or
            $entry.GetAttribute('AppType') -ne '.Net' -or
            $null -eq $entryReq -or
            $entryReq.GetAttribute('Platform') -ne 'AutoCAD*' -or
            $entryReq.GetAttribute('SeriesMin') -ne $series -or
            $entryReq.GetAttribute('SupportPath') -ne "./Contents/$year") {
            throw "AutoCAD $year ComponentEntry/RuntimeRequirements are invalid."
        }
        $dll = Join-Path $Destination ($module.Substring(2).Replace('/', '\'))
        if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw "Payload is missing $module" }
    }
}

function New-InstallerPayloadFromYearBuilds {
    param(
        [ValidateSet('Debug', 'Release')]
        [string]$BuildType = 'Release',
        [Parameter(Mandatory = $true)]
        [string]$Destination,
        [string]$Version = '1.0.0',
        [ValidateRange(2019, 2027)]
        [int[]]$Years = @(2019..2027)
    )

    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "MSI ProductVersion must be major.minor.build (got '$Version')."
    }

    $yearBundles = @{}
    foreach ($year in $Years) {
        $configuration = "$BuildType`A$($year - 2000)"
        $settings = & (Join-Path $PSScriptRoot 'Get-AutoCADBuildSettings.ps1') -Configuration $configuration
        $bundle = Join-Path $settings.TargetDir 'AutoBIMFusion.bundle'
        if (-not (Test-Path -LiteralPath (Join-Path $bundle 'PackageContents.xml') -PathType Leaf)) {
            throw "Missing bundle for $configuration. Expected: $bundle"
        }
        Assert-AutoCADPluginContents -ContentsDir (Join-Path $bundle 'Contents') -Context "AutoCAD $year bundle" -RequireAssemblies
        $yearBundles[$year] = $bundle
    }

    Write-Host "Staging multi-version bundle: $Destination"
    [void](New-InstallerPayload -Destination $Destination -Version $Version -YearBundles $yearBundles)
}
