Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'New-InstallerPayload.ps1')

$root = Join-Path ([IO.Path]::GetTempPath()) ('AutoBIMFusion-msi-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null

function Get-MsiColumn {
    param($Database, [string]$Sql)
    $view = $Database.OpenView($Sql)
    $view.Execute() | Out-Null
    $values = @()
    while ($record = $view.Fetch()) {
        $values += $record.StringData(1)
    }
    $view.Close()
    $values
}

try {
    $yearBundles = @{}
    foreach ($year in 2019..2027) { $yearBundles[$year] = New-InstallerTestBundle -Root $root -Year $year }
    $payload = Join-Path $root 'AutoBIMFusion.bundle'
    [void](New-InstallerPayload -Destination $payload -Version '1.0.0' -YearBundles $yearBundles)

    $wixProject = Join-Path $repoRoot 'installer\AutoBIMFusion.Installer.wixproj'
    & dotnet build $wixProject -c Release -p:Platform=x64 '-p:Version=1.0.0' `
        "-p:PayloadDir=$payload" "-p:OutputPath=$root\msi\" "-p:IntermediateOutputPath=$root\obj\" -v:q
    if ($LASTEXITCODE -ne 0) { throw 'WiX MSI build failed.' }
    $msi = Get-ChildItem -LiteralPath (Join-Path $root 'msi') -Recurse -Filter 'AutoBIMFusion.msi' -File |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $msi) { throw 'Fixture MSI was not produced.' }

    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = $installer.OpenDatabase($msi, 0)
    $upgradeAttributes = [int](Get-MsiColumn $db "SELECT Attributes FROM Upgrade" | Select-Object -First 1)
    if (($upgradeAttributes -band 512) -eq 0) {
        throw "Upgrade table is missing VersionMaxInclusive (AllowSameVersionUpgrades). Attributes=$upgradeAttributes"
    }
    $reboot = Get-MsiColumn $db "SELECT Value FROM Property WHERE Property='REBOOT'"
    $restartManager = Get-MsiColumn $db "SELECT Value FROM Property WHERE Property='MSIRESTARTMANAGERCONTROL'"
    if ($reboot -notcontains 'ReallySuppress' -or $restartManager -notcontains 'Disable') {
        throw "Uninstall reboot suppression is missing. REBOOT=$reboot MSIRESTARTMANAGERCONTROL=$restartManager"
    }
    $installFolder = Get-MsiColumn $db "SELECT Name FROM Registry WHERE Name='InstallFolder'"
    if ($installFolder.Count -eq 0) { throw 'InstallFolder is not remembered in the Registry table.' }
    $removeFolderEx = Get-MsiColumn $db "SELECT Property FROM Wix4RemoveFolderEx"
    if ($removeFolderEx -notcontains 'INSTALLFOLDER_PROP' -or
        $removeFolderEx -notcontains 'PERUSERPLUGINFOLDER' -or
        $removeFolderEx -notcontains 'PROGRAMDATAPLUGINFOLDER') {
        throw "RemoveFolderEx properties missing: $($removeFolderEx -join ', ')"
    }
    $closeApps = Get-MsiColumn $db "SELECT Target FROM Wix4CloseApplication"
    if ($closeApps -notcontains 'acad.exe' -or $closeApps -notcontains 'accoreconsole.exe') {
        throw "CloseApplication targets missing: $($closeApps -join ', ')"
    }
    Write-Host "PASS: same-version upgrade, remembered install folder, uninstall folder cleanup, AutoCAD close. MSI: $msi"
}
finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
}
