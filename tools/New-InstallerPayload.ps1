Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'AutoCADPluginBundle.ps1')

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

function Assert-InstallerPayload {
    param(
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][ValidateRange(2019, 2027)][int]$Year
    )
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "MSI ProductVersion must be major.minor.build (got '$Version')."
    }
    [xml]$manifest = Get-Content -LiteralPath (Join-Path $Destination 'PackageContents.xml') -Raw -Encoding UTF8
    $package = $manifest.SelectSingleNode('/ApplicationPackage')
    if ($null -eq $package -or
        $package.GetAttribute('AppVersion') -ne $Version -or
        $package.GetAttribute('Name') -ne "AutoBIMFusion $Year" -or
        $package.GetAttribute('ProductCode') -ne "{4F3E42D4-4D2F-4A2E-8A2A-BB771A0D$Year}") {
        throw "Unexpected package identity for AutoCAD $Year version $Version."
    }
    $components = @($package.SelectNodes('Components'))
    $entries = @($package.SelectNodes('Components/ComponentEntry'))
    if ($components.Count -ne 1 -or $entries.Count -ne 1 -or
        $entries[0].GetAttribute('ModuleName') -ne "./Contents/$Year/AutoBIMFusion.dll") {
        throw "Expected only AutoCAD $Year autoload registration in installer payload."
    }
    $entry = $entries[0]
    if ($entry.GetAttribute('AppName') -ne 'AutoBIMFusion' -or
        $entry.GetAttribute('AppType') -ne '.Net' -or
        $entry.GetAttribute('LoadOnAutoCADStartup') -ne 'true') {
        throw "AutoCAD $Year entry must set AppName=AutoBIMFusion, AppType=.Net and LoadOnAutoCADStartup=true."
    }
    $series = @('R23.0', 'R23.1', 'R24.0', 'R24.1', 'R24.2', 'R24.3', 'R25.0', 'R25.1', 'R26.0')[$Year - 2019]
    foreach ($node in @($components[0], $entry)) {
        $requirements = @($node.SelectNodes('RuntimeRequirements'))
        if ($requirements.Count -ne 1) { throw 'Expected exactly one RuntimeRequirements element per registration level.' }
        $req = $requirements[0]
        if ($req.GetAttribute('OS') -ne 'Win64' -or
            $req.GetAttribute('Platform') -ne 'AutoCAD*' -or
            $req.GetAttribute('SeriesMin') -ne $series -or
            $req.GetAttribute('SeriesMax') -ne $series -or
            $req.GetAttribute('SupportPath') -ne "./Contents/$Year") {
            throw "AutoCAD $Year RuntimeRequirements must target Win64, AutoCAD*, $series and ./Contents/$Year."
        }
    }
    Assert-AutoCADPluginContents -ContentsDir (Join-Path $Destination "Contents\$Year") `
        -Context "AutoCAD $Year installer payload" -RequireAssemblies
}

function New-InstallerPayload {
    param(
        [Parameter(Mandatory = $true)][string]$SourceBundle,
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][ValidateRange(2019, 2027)][int]$Year
    )
    $source = (Resolve-Path -LiteralPath $SourceBundle).ProviderPath.TrimEnd('\')
    $destination = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
    if ([IO.Path]::GetFileName($destination) -ne 'AutoBIMFusion.bundle') {
        throw 'Payload destination must be named AutoBIMFusion.bundle.'
    }
    if ($source -eq $destination -or
        $destination.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $source.StartsWith($destination + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Source and payload directories must not overlap.'
    }
    [xml]$manifest = Get-Content -LiteralPath (Join-Path $source 'PackageContents.xml') -Raw -Encoding UTF8
    $entries = @($manifest.SelectNodes('/ApplicationPackage/Components/ComponentEntry'))
    if ($entries.Count -ne 1 -or $entries[0].GetAttribute('ModuleName') -ne './Contents/AutoBIMFusion.dll') {
        throw 'Source must contain one flat plugin entry point.'
    }
    $package = $manifest.DocumentElement
    $package.SetAttribute('Name', "AutoBIMFusion $Year")
    $package.SetAttribute('ProductCode', "{4F3E42D4-4D2F-4A2E-8A2A-BB771A0D$Year}")
    $package.SetAttribute('AppVersion', $Version)
    $package.SetAttribute('Icon', "./Contents/$Year/Resources/icon-merge-dwg-32.png")
    $entries[0].SetAttribute('ModuleName', "./Contents/$Year/AutoBIMFusion.dll")
    foreach ($req in $manifest.SelectNodes('/ApplicationPackage/Components/RuntimeRequirements | /ApplicationPackage/Components/ComponentEntry/RuntimeRequirements')) {
        $req.SetAttribute('Platform', 'AutoCAD*')
        $req.SetAttribute('SupportPath', "./Contents/$Year")
    }

    $parent = [IO.Path]::GetDirectoryName($destination)
    $prepared = [IO.Path]::GetFullPath((Join-Path $parent ('.AutoBIMFusion-payload-' + [Guid]::NewGuid().ToString('N'))))
    # Cleanup is restricted to this generated sibling; the publisher owns replacement and rollback.
    if ([IO.Path]::GetDirectoryName($prepared) -ne $parent) { throw 'Payload staging path escapes its parent.' }
    try {
        $yearDir = Join-Path $prepared "Contents\$Year"
        New-Item -ItemType Directory -Path $yearDir -Force | Out-Null
        Copy-Item -Path (Join-Path $source 'Contents\*') -Destination $yearDir -Recurse -Force
        Get-ChildItem -LiteralPath $yearDir -Filter '*.pdb' -File -Recurse | Remove-Item -Force
        Save-InstallerManifest -Manifest $manifest -Path (Join-Path $prepared 'PackageContents.xml')
        Assert-InstallerPayload -Destination $prepared -Version $Version -Year $Year
        & (Join-Path $PSScriptRoot 'Publish-AutoCADBundle.ps1') -SourceBundle $prepared -TargetBundle $destination
    }
    finally {
        if (Test-Path -LiteralPath $prepared) { Remove-Item -LiteralPath $prepared -Recurse -Force }
    }
    $destination
}
