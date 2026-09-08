Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'MergeDwgRecentDocuments.ps1')

# Real cross-process exclusion; only a named mutex is touched, never AutoCAD's profile.
$gate = Enter-BatchHistoryGate
$job = $null
try {
    $job = Start-Job -ScriptBlock {
        $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $other = New-Object Threading.Mutex($false, "Global\AutoBIMFusion-RecentDocuments-$sid")
        try {
            $entered = $other.WaitOne(0)
            if ($entered) { $other.ReleaseMutex() }
            return $entered
        }
        finally { $other.Dispose() }
    }
    $null = Wait-Job $job -Timeout 20
    if ($job.State -ne 'Completed') { throw 'Mutex test child did not complete.' }
    $entered = Receive-Job $job -ErrorAction Stop
    if ($entered -ne $false) { throw 'Another process acquired the occupied history gate.' }
}
finally {
    $gate.ReleaseMutex()
    $gate.Dispose()
    if ($null -ne $job) { Remove-Job $job -Force }
}

# Mock the OS boundary below: exercise cleanup decisions without changing real registry values.
$script:released = 0
function Enter-BatchHistoryGate {
    $mock = [pscustomobject]@{}
    $mock | Add-Member ScriptMethod ReleaseMutex { $script:released++ }
    $mock | Add-Member ScriptMethod Dispose { }
    return $mock
}
function Get-Process {
    param($ErrorAction)
    $script:processChecks++
    if ($script:processError) { throw 'Process enumeration denied.' }
    if ($script:busy -or ($script:appearAfter -gt 0 -and $script:processChecks -ge $script:appearAfter)) {
        [pscustomobject]@{ ProcessName = 'acad' }
    }
}
function Test-Path { param($LiteralPath, $ErrorAction) return $script:exists }
function Get-ChildItem {
    param($LiteralPath, $ErrorAction)
    if ($LiteralPath -ne 'HKCU:\Software\Autodesk\AutoCAD\R25.1') { throw 'Wrong release targeted.' }
    $product = [pscustomobject]@{ PSPath = 'fixture-product' }
    $product | Add-Member ScriptMethod GetValue { param($name) return $script:location }
    return $product
}
function Get-Item {
    param($LiteralPath, $ErrorAction)
    if ($LiteralPath -eq $script:exe) {
        return [pscustomobject]@{
            FullName = $script:exe
            VersionInfo = [pscustomobject]@{ ProductMajorPart = 25; ProductMinorPart = 1 }
        }
    }
    $history = [pscustomobject]@{}
    $history | Add-Member ScriptMethod GetValueNames { return @($script:values.Keys) }
    return $history
}
function Remove-ItemProperty {
    param($LiteralPath, $Name, $ErrorAction)
    if ($script:writeError) { throw 'Registry write denied.' }
    if ($LiteralPath -ne (Join-Path 'fixture-product' 'Recent File List')) { throw 'Wrong key targeted.' }
    $script:values.Remove($Name)
}
function Reset-Fixture {
    $script:exe = Join-Path ([IO.Path]::GetTempPath()) 'fixture-acad\acad.exe'
    $script:location = Split-Path $script:exe -Parent
    $script:values = @{ File1 = 'a.dwg'; File20 = 'b.dwg'; PinnedFile1 = 'keep.dwg'; Count = 2; FileOther = 'keep' }
    $script:exists = $true
    $script:busy = $false
    $script:processError = $false
    $script:writeError = $false
    $script:processChecks = 0
    $script:appearAfter = 0
}
function Assert-Result {
    param([string]$Status, [int]$Removed)
    $before = $script:released
    $result = Clear-BatchRecentDocuments -AutoCADExe $script:exe
    if ($result.Status -ne $Status -or $result.Removed -ne $Removed) {
        throw "Unexpected cleanup result: $($result | ConvertTo-Json -Compress)"
    }
    if ($script:released -ne $before + 1) { throw 'History gate leaked.' }
}

Reset-Fixture
Assert-Result 'Completed' 2
if ($script:values.Keys.Count -ne 3 -or -not $script:values.ContainsKey('PinnedFile1')) { throw 'Unrelated values changed.' }
Assert-Result 'Completed' 0
Reset-Fixture
$script:busy = $true
Assert-Result 'Skipped' 0
if ($script:values.Keys.Count -ne 5) { throw 'Busy host history changed.' }
Reset-Fixture
$script:appearAfter = 2
Assert-Result 'Skipped' 0
Reset-Fixture
$script:exists = $false
Assert-Result 'Skipped' 0
Reset-Fixture
$script:location = Join-Path ([IO.Path]::GetTempPath()) 'different-acad'
Assert-Result 'Skipped' 0
Reset-Fixture
$script:processError = $true
Assert-Result 'Failed' 0
Reset-Fixture
$script:writeError = $true
Assert-Result 'Failed' 0

# Repeated large batches do not accumulate cleanup state or leak the mutex.
foreach ($iteration in 1..1000) {
    Reset-Fixture
    Assert-Result 'Completed' 2
}
Write-Host 'PASS: process exclusion, safe skips, registry failures, idempotence, 1000 cleanup cycles.'

# Load only the scheduler function, never execute the batch launcher's top level.
$tokens = $null
$errors = $null
$launcher = Join-Path $PSScriptRoot 'Start-MergeDwgBatch.ps1'
$ast = [Management.Automation.Language.Parser]::ParseFile($launcher, [ref]$tokens, [ref]$errors)
if ($errors.Count -gt 0) { throw "Launcher syntax errors: $errors" }
$functionAst = $ast.Find({ param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Complete-FinishedProcesses'
}, $true)
. ([scriptblock]::Create($functionAst.Extent.Text))
function Stop-Process {
    param($Id, [switch]$Force, $ErrorAction)
    if ($script:stopFails) { throw 'Stop denied.' }
}
foreach ($scenario in @('stop-denied', 'exit-pending', 'exited')) {
    $script:stopFails = $scenario -eq 'stop-denied'
    $script:exitConfirmed = $scenario -eq 'exited'
    $process = [pscustomobject]@{ Id = 123; HasExited = $false }
    $process | Add-Member ScriptMethod WaitForExit {
        param($milliseconds)
        $this.HasExited = $script:exitConfirmed
        return $this.HasExited
    }
    $item = [pscustomobject]@{ Process = $process; StartedAt = (Get-Date).AddMinutes(-5); TimedOut = $false }
    $active = New-Object Collections.ArrayList
    $completed = New-Object Collections.ArrayList
    [void]$active.Add($item)
    $failed = $false
    try { Complete-FinishedProcesses -ActiveProcesses $active -CompletedProcesses $completed -TimeoutMinutes 1 }
    catch { $failed = $true }
    if ($script:exitConfirmed) {
        if ($failed -or $active.Count -ne 0 -or $completed.Count -ne 1 -or -not $item.TimedOut) {
            throw 'Exited timeout worker was not accounted for correctly.'
        }
    }
    elseif (-not $failed -or $active.Count -ne 1 -or $completed.Count -ne 0) {
        throw "Live worker slot was incorrectly freed: $scenario"
    }
}
Write-Host 'PASS: failed termination and pending exit retain slots; confirmed exit releases a slot.'
