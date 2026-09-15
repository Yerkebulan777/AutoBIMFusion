Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'New-InstallerPayload.ps1')
. (Join-Path $PSScriptRoot 'TestFixtures\InstallerBundle.ps1')

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
    $view.Close() | Out-Null
    $values
}

try {
    $snapshots = @{}
    foreach ($year in @(2020, 2026)) {
        $source = New-InstallerTestBundle -Root $root -Year $year
        $payload = Join-Path $root "$year\AutoBIMFusion.bundle"
        [void](New-InstallerPayload -SourceBundle $source -Destination $payload -Version '1.0.0' -Year $year)

        $wixProject = Join-Path $repoRoot 'installer\AutoBIMFusion.Installer.wixproj'
        & dotnet build $wixProject -c "ReleaseA$($year - 2000)" -p:Platform=x64 '-p:Version=1.0.0' `
            "-p:PayloadDir=$payload" "-p:OutputPath=$root\msi\$year\" "-p:IntermediateOutputPath=$root\obj\$year\" `
            '-p:SkipInstallerDependencyBuild=true' '-p:PublishMsi=false' -v:q
        if ($LASTEXITCODE -ne 0) { throw 'WiX MSI build failed.' }
        $msi = Get-ChildItem -LiteralPath (Join-Path $root "msi\$year") -Recurse -Filter "AutoBIMFusion-AutoCAD$year.msi" -File |
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
        $installFolder = @(Get-MsiColumn $db "SELECT Name FROM Registry WHERE Name='InstallFolder'")
        if ($installFolder.Count -eq 0) { throw 'InstallFolder is not remembered in the Registry table.' }
        $removeFolderEx = Get-MsiColumn $db "SELECT Property FROM Wix4RemoveFolderEx"
        if ($removeFolderEx -notcontains 'INSTALLFOLDER_PROP' -or
            $removeFolderEx -contains 'PERUSERPLUGINFOLDER' -or
            $removeFolderEx -notcontains 'PROGRAMDATAPLUGINFOLDER') {
            throw "RemoveFolderEx properties missing: $($removeFolderEx -join ', ')"
        }
        $closeApps = Get-MsiColumn $db "SELECT Target FROM Wix4CloseApplication"
        if ($closeApps -notcontains 'acad.exe' -or $closeApps -notcontains 'accoreconsole.exe') {
            throw "CloseApplication targets missing: $($closeApps -join ', ')"
        }
        $copyAction = @(Get-MsiColumn $db "SELECT Action FROM CustomAction WHERE Action='CopyBundleToProgramData'")
        if ($copyAction.Count -eq 0) {
            throw 'MSI is missing CopyBundleToProgramData (ProgramData Autoloader mirror).'
        }
        $registryKeys = @(Get-MsiColumn $db 'SELECT `Key` FROM Registry')
        if (@($registryKeys | Where-Object { $_ -ne "Software\AutoBIMFusion\$year" }).Count -gt 0) {
            throw "Registry writes are not scoped to ${year}: $($registryKeys -join ', ')"
        }
        foreach ($directory in @('INSTALLFOLDER', 'PROGRAMDATAINSTALLFOLDER')) {
            $names = @(Get-MsiColumn $db "SELECT DefaultDir FROM Directory WHERE Directory='$directory'")
            if ($names.Count -ne 1 -or ($names[0] -split '\|')[-1] -ne "AutoBIMFusion-$year.bundle") {
                throw "Directory $directory is not scoped to $year."
            }
        }
        $programData = @(Get-MsiColumn $db "SELECT Value FROM Property WHERE Property='PROGRAMDATAPLUGINFOLDER'")
        if ($programData.Count -ne 1 -or $programData[0] -notlike "*\AutoBIMFusion-$year.bundle") {
            throw "Cleanup path is not scoped to $year."
        }
        $snapshots[$year] = @{
            UpgradeCodes = @(Get-MsiColumn $db 'SELECT UpgradeCode FROM Upgrade')
            ComponentIds = @(Get-MsiColumn $db 'SELECT ComponentId FROM Component')
            ProductCode = @(Get-MsiColumn $db "SELECT Value FROM Property WHERE Property='ProductCode'")[0]
        }
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($db)
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
        Write-Host "PASS: AutoCAD $year upgrade, cleanup and paths. MSI: $msi"
    }
    if ($snapshots[2020].ProductCode -eq $snapshots[2026].ProductCode) { throw 'Years share a ProductCode.' }
    foreach ($id in $snapshots[2020].UpgradeCodes) {
        if ($snapshots[2026].UpgradeCodes -contains $id) { throw 'Installing one year would replace another year.' }
    }
    foreach ($id in $snapshots[2020].ComponentIds) {
        if ($snapshots[2026].ComponentIds -contains $id) { throw 'Different years share component ownership.' }
    }
    Write-Host 'PASS: AutoCAD 2020 and 2026 MSI identities and component ownership are independent.'
}
finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
}
