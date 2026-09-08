# This file is dot-sourced by the batch coordinator. Never run cleanup in a worker.
function Enter-BatchHistoryGate {
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $mutex = New-Object Threading.Mutex($false, "Global\AutoBIMFusion-RecentDocuments-$sid")
    try {
        try { $acquired = $mutex.WaitOne(30000) }
        catch [Threading.AbandonedMutexException] { $acquired = $true }
        if (-not $acquired) { throw 'Timed out waiting for the AutoCAD history gate.' }
        return $mutex
    }
    catch { $mutex.Dispose(); throw }
}

function Clear-BatchRecentDocuments {
    param([Parameter(Mandatory = $true)][string]$AutoCADExe)

    $result = [pscustomobject]@{ Status = 'Skipped'; Removed = 0; Message = '' }
    $gate = $null
    try {
        $gate = Enter-BatchHistoryGate
        # Enumerate all processes so access/query failures fail closed. Do not rely on
        # a worker's timeout flag: Stop-Process can fail or shutdown can still be pending.
        if (@(Get-Process -ErrorAction Stop | Where-Object { $_.ProcessName -in @('acad', 'accoreconsole') }).Count -gt 0) {
            $result.Message = 'AutoCAD is still running; recent documents were left unchanged.'
            return $result
        }

        $exe = Get-Item -LiteralPath $AutoCADExe -ErrorAction Stop
        $release = 'R{0}.{1}' -f $exe.VersionInfo.ProductMajorPart, $exe.VersionInfo.ProductMinorPart
        $root = "HKCU:\Software\Autodesk\AutoCAD\$release"
        if (-not (Test-Path -LiteralPath $root -ErrorAction Stop)) {
            $result.Message = 'No registry history found for the selected AutoCAD release.'
            return $result
        }

        $matched = $false
        foreach ($product in Get-ChildItem -LiteralPath $root -ErrorAction Stop) {
            $location = $product.GetValue('AcadLocation')
            if ([string]::IsNullOrWhiteSpace($location)) { continue }
            $installedExe = [IO.Path]::GetFullPath((Join-Path $location 'acad.exe'))
            if (-not [string]::Equals($installedExe, $exe.FullName, [StringComparison]::OrdinalIgnoreCase)) { continue }
            $historyPath = Join-Path $product.PSPath 'Recent File List'
            if (-not (Test-Path -LiteralPath $historyPath -ErrorAction Stop)) { continue }
            $matched = $true
            $history = Get-Item -LiteralPath $historyPath -ErrorAction Stop
            foreach ($name in $history.GetValueNames()) {
                # Keep pinned/settings/unknown metadata; never remove the key itself.
                if ($name -notmatch '^File[0-9]+$') { continue }
                if (@(Get-Process -ErrorAction Stop | Where-Object { $_.ProcessName -in @('acad', 'accoreconsole') }).Count -gt 0) {
                    $result.Message = 'AutoCAD started during cleanup; further changes were skipped.'
                    return $result
                }
                Remove-ItemProperty -LiteralPath $historyPath -Name $name -ErrorAction Stop
                $result.Removed++
            }
        }
        if ($matched) {
            $result.Status = 'Completed'
            $result.Message = 'Recognized recent-file entries cleared.'
        }
        else { $result.Message = 'No matching Recent File List found; unknown history formats were left unchanged.' }
    }
    catch {
        $result.Status = 'Failed'
        $result.Message = $_.Exception.Message
    }
    finally {
        if ($null -ne $gate) { $gate.ReleaseMutex(); $gate.Dispose() }
    }
    return $result
}
