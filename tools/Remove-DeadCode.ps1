<#
.SYNOPSIS
    Interactively removes dead code confirmed in dead-code-candidates.md.

.DESCRIPTION
    Safety-first workflow:
      1. Creates git branch 'cleanup/dead-code' (if not already on it)
      2. Presents each High-confidence candidate
      3. Removes the symbol from source (or whole file if it's the only content)
      4. Runs dotnet build to verify — auto-reverts if build breaks
      5. Commits each successful removal separately

.PARAMETER DryRun
    Show what would be removed, but make no changes.

.PARAMETER Configuration
    Build configuration to use for verification (default: DebugA26).

.PARAMETER AutoConfirm
    Skip per-item confirmation prompts (remove all High-confidence items automatically).

.EXAMPLE
    # Preview only
    .\tools\Remove-DeadCode.ps1 -DryRun

    # Interactive removal
    .\tools\Remove-DeadCode.ps1

    # Auto-remove all High-confidence items
    .\tools\Remove-DeadCode.ps1 -AutoConfirm
#>

param(
    [switch]$DryRun,
    [string]$Configuration = "DebugA26",
    [switch]$AutoConfirm
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot   = Split-Path -Parent $scriptRoot

# High-confidence dead code — manually verified by GitNexus + grep
# Add entries here after cross-referencing with Roslynator output.
# Format: @{ File = relative path; Symbol = name; Line = approx start line; Reason = why }
$candidates = @(
    @{
        File   = "src\AutoBIMFusion.Merge\Combine\Layouts\DimensionStyleDiagnosticUtils.cs"
        Symbol = "AppendDimStyleProperties"
        Line   = 239
        Reason = "private static — dead duplicate of FormatUtils.AppendDimStyleProperties, zero callers"
    },
    @{
        File   = "src\AutoBIMFusion.Merge\Combine\Layouts\DimensionStyleDiagnosticUtils.cs"
        Symbol = "AppendProperties"
        Line   = 286
        Reason = "private static — dead duplicate of FormatUtils.AppendProperties, zero callers"
    },
    @{
        File   = "src\AutoBIMFusion.Merge\Combine\Layouts\StyleUnificationService.cs"
        Symbol = "GetOrCreateStandardDimensionStyle"
        Line   = 79
        Reason = "internal static — grep confirms zero callers across entire solution"
    }
)

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

function Write-Header([string]$msg) {
    Write-Host ""
    Write-Host ("=" * 60) -ForegroundColor DarkGray
    Write-Host "  $msg" -ForegroundColor Cyan
    Write-Host ("=" * 60) -ForegroundColor DarkGray
}

function Confirm-Action([string]$prompt) {
    if ($AutoConfirm) { return $true }
    $reply = Read-Host "$prompt [y/N]"
    return $reply -match '^[Yy]'
}

function Build-Verify() {
    Write-Host "  Verifying build..." -ForegroundColor Gray
    $pluginProj = Join-Path $repoRoot "src\AutoBIMFusion.Plugin\AutoBIMFusion.Plugin.csproj"
    $output = dotnet build $pluginProj -c $Configuration --no-incremental -nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  BUILD FAILED." -ForegroundColor Red
        Write-Host ($output | Select-String "error" | Out-String) -ForegroundColor Red
        return $false
    }
    Write-Host "  Build OK." -ForegroundColor Green
    return $true
}

function Strip-CodeNoise([string]$line) {
    # Remove single-line comments
    $line = $line -replace '//.*$', ''
    # Remove double-quoted string literals (handles simple cases; \" inside strings not counted)
    $line = $line -replace '"[^"]*"', '""'
    # Remove single-quoted char literals like '{' or '}'
    $line = $line -replace "'\.'", "'x'"
    # Remove verbatim strings @"..."  (single-line portion only)
    $line = $line -replace '@"[^"]*"', '@""'
    return $line
}

function Get-MethodBounds([string]$filePath, [string]$symbolName, [int]$approxLine) {
    $lines  = Get-Content $filePath
    $total  = $lines.Count

    # Find the line that declares the method (search around approxLine ± 20)
    $searchStart = [Math]::Max(0, $approxLine - 20)
    $searchEnd   = [Math]::Min($total - 1, $approxLine + 20)

    $declLine = -1
    for ($i = $searchStart; $i -le $searchEnd; $i++) {
        if ($lines[$i] -match "\b$([regex]::Escape($symbolName))\b") {
            $declLine = $i
            break
        }
    }

    if ($declLine -lt 0) {
        # Fall back: search whole file
        for ($i = 0; $i -lt $total; $i++) {
            if ($lines[$i] -match "\b$([regex]::Escape($symbolName))\b") {
                $declLine = $i
                break
            }
        }
    }

    if ($declLine -lt 0) {
        return $null
    }

    # Walk back to find any attribute / doc comment lines
    $methodStart = $declLine
    for ($i = $declLine - 1; $i -ge 0; $i--) {
        $trimmed = $lines[$i].Trim()
        if ($trimmed -match '^\[' -or $trimmed -match '^///' -or $trimmed -eq '') {
            $methodStart = $i
        } else {
            break
        }
    }

    # Find opening brace — strip strings/comments first to avoid false brace counts
    $braceDepth = 0
    $bodyStart  = -1
    for ($i = $declLine; $i -lt $total; $i++) {
        $stripped = Strip-CodeNoise $lines[$i]
        foreach ($ch in $stripped.ToCharArray()) {
            if ($ch -eq '{') {
                $braceDepth++
                $bodyStart = $i
            } elseif ($ch -eq '}') {
                $braceDepth--
                if ($braceDepth -eq 0 -and $bodyStart -ge 0) {
                    return @{ Start = $methodStart; End = $i }
                }
            }
        }
    }

    return $null
}

function Remove-Symbol([string]$filePath, [string]$symbolName, [int]$approxLine) {
    $bounds = Get-MethodBounds $filePath $symbolName $approxLine
    if (-not $bounds) {
        Write-Host "  Could not locate '$symbolName' in $filePath — skipping." -ForegroundColor Yellow
        return $false
    }

    $lines      = Get-Content $filePath
    $startIdx   = $bounds.Start
    $endIdx     = $bounds.End

    Write-Host "  Removing lines $($startIdx + 1)..$($endIdx + 1) ($symbolName)" -ForegroundColor Yellow

    $newLines = @()
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($i -lt $startIdx -or $i -gt $endIdx) {
            $newLines += $lines[$i]
        }
    }

    Set-Content -Path $filePath -Value $newLines -Encoding UTF8
    return $true
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

Write-Header "AutoBIMFusion Dead Code Removal"

if ($DryRun) {
    Write-Host "  DRY RUN — no files will be modified." -ForegroundColor Yellow
}

# Ensure we're in the repo root
Set-Location $repoRoot

# Git safety: create or switch to cleanup branch
if (-not $DryRun) {
    $currentBranch = git rev-parse --abbrev-ref HEAD
    if ($currentBranch -ne 'cleanup/dead-code') {
        $branchExists = git branch --list 'cleanup/dead-code'
        if ($branchExists) {
            Write-Host "Switching to existing branch 'cleanup/dead-code'..." -ForegroundColor Gray
            git checkout cleanup/dead-code
        } else {
            Write-Host "Creating branch 'cleanup/dead-code'..." -ForegroundColor Gray
            git checkout -b cleanup/dead-code
        }
    }
}

$removedCount = 0
$failedCount  = 0

foreach ($c in $candidates) {
    $filePath = Join-Path $repoRoot $c.File
    if (-not (Test-Path $filePath)) {
        Write-Host "File not found: $($c.File) — skipping." -ForegroundColor Yellow
        continue
    }

    Write-Header "$($c.Symbol)"
    Write-Host "  File:   $($c.File)" -ForegroundColor White
    Write-Host "  Line:   ~$($c.Line)" -ForegroundColor White
    Write-Host "  Reason: $($c.Reason)" -ForegroundColor Gray

    if ($DryRun) {
        Write-Host "  [DRY RUN] Would remove this symbol." -ForegroundColor DarkYellow
        continue
    }

    if (-not (Confirm-Action "  Remove '$($c.Symbol)'?")) {
        Write-Host "  Skipped." -ForegroundColor Gray
        continue
    }

    # Backup: record git state for rollback
    $originalContent = Get-Content $filePath -Raw

    $removed = Remove-Symbol $filePath $c.Symbol $c.Line

    if (-not $removed) {
        $failedCount++
        continue
    }

    # Verify build
    if (-not (Build-Verify)) {
        Write-Host "  Reverting $($c.File)..." -ForegroundColor Red
        Set-Content -Path $filePath -Value $originalContent -Encoding UTF8 -NoNewline
        Write-Host "  Reverted. Build restored." -ForegroundColor Yellow
        $failedCount++
        continue
    }

    # Commit this removal
    git add $c.File
    $msg = "cleanup: remove dead method $($c.Symbol)`n`n$($c.Reason)"
    git commit -m $msg
    Write-Host "  Committed removal of '$($c.Symbol)'." -ForegroundColor Green
    $removedCount++
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Header "Summary"
if ($DryRun) {
    Write-Host "  DRY RUN complete. $($candidates.Count) candidate(s) would be processed."
} else {
    Write-Host "  Removed:  $removedCount" -ForegroundColor Green
    Write-Host "  Failed:   $failedCount"  -ForegroundColor $(if ($failedCount -gt 0) { 'Red' } else { 'Gray' })
    Write-Host "  Skipped:  $($candidates.Count - $removedCount - $failedCount)" -ForegroundColor Gray
    if ($removedCount -gt 0) {
        Write-Host ""
        Write-Host "  Run smoke test: open AutoCAD and invoke MERGEDWG command." -ForegroundColor Yellow
        Write-Host "  Then merge branch: git checkout main && git merge cleanup/dead-code" -ForegroundColor Yellow
    }
}
