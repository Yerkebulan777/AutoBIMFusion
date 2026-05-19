<#
.SYNOPSIS
    Finds dead code in AutoBIMFusion not reachable from MERGEDWG / MERGEDWG_BATCH.

.DESCRIPTION
    Two-pass analysis:
      Pass 1 — GitNexus (already complete): results in tools/dead-code-candidates.md
      Pass 2 — Roslynator: finds unused private members per project, appends to the same file.

    Run from repo root or tools/ directory.

.EXAMPLE
    .\tools\Find-DeadCode.ps1
    .\tools\Find-DeadCode.ps1 -Configuration DebugA27
#>

param(
    [string]$Configuration = "DebugA26",
    [switch]$SkipRoslynator
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot  = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot    = Split-Path -Parent $scriptRoot
$reportFile  = Join-Path $scriptRoot "dead-code-candidates.md"

$projects = @(
    "src\AutoBIMFusion.Common\AutoBIMFusion.Common.csproj",
    "src\AutoBIMFusion.Merge\AutoBIMFusion.Merge.csproj",
    "src\AutoBIMFusion.Plugin\AutoBIMFusion.Plugin.csproj"
)

# ---------------------------------------------------------------------------
# Helper
# ---------------------------------------------------------------------------
function Write-Section([string]$title) {
    Write-Host ""
    Write-Host "=== $title ===" -ForegroundColor Cyan
}

# ---------------------------------------------------------------------------
# Roslynator install check
# ---------------------------------------------------------------------------
if (-not $SkipRoslynator) {
    Write-Section "Checking Roslynator"
    $roslynatorPath = (Get-Command roslynator -ErrorAction SilentlyContinue)?.Source

    if (-not $roslynatorPath) {
        Write-Host "Roslynator not found. Installing..." -ForegroundColor Yellow
        dotnet tool install -g roslynator.dotnet.cli
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Install failed. Rerun with -SkipRoslynator to skip Roslynator pass."
            exit 1
        }
        Write-Host "Roslynator installed." -ForegroundColor Green
    } else {
        Write-Host "Roslynator found: $roslynatorPath" -ForegroundColor Green
    }
}

# ---------------------------------------------------------------------------
# Pass 2: Roslynator per-project
# ---------------------------------------------------------------------------
if (-not $SkipRoslynator) {
    Write-Section "Pass 2 — Roslynator (unused private members)"

    $roslynatorRaw = Join-Path $scriptRoot "roslynator-unused-raw.txt"
    if (Test-Path $roslynatorRaw) { Remove-Item $roslynatorRaw }

    foreach ($proj in $projects) {
        $projPath = Join-Path $repoRoot $proj
        Write-Host "  Analyzing $proj ..." -ForegroundColor Gray

        # Roslynator find-symbols finds unused symbols (unreferenced types/members).
        # We focus on: unused members (private fields, methods, properties) and unused types.
        $output = roslynator find-symbols `
            --project $projPath `
            --symbol-kind all `
            --visibility private,internal `
            2>&1

        if ($LASTEXITCODE -ne 0) {
            Write-Warning "  Roslynator failed for $proj. Continuing."
        } else {
            Add-Content -Path $roslynatorRaw -Value "### $proj"
            Add-Content -Path $roslynatorRaw -Value ($output | Out-String)
            Add-Content -Path $roslynatorRaw -Value ""
        }
    }

    Write-Host "Raw Roslynator output saved to: $roslynatorRaw" -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# Update report header timestamp
# ---------------------------------------------------------------------------
Write-Section "Updating report timestamp"

$header = @"

---
<!-- Roslynator pass run: $(Get-Date -Format 'yyyy-MM-dd HH:mm') | Config: $Configuration -->
<!-- Raw Roslynator output: tools/roslynator-unused-raw.txt -->

## Pass 2 — Roslynator Results

See ``tools\roslynator-unused-raw.txt`` for full output.
Cross-reference with the GitNexus candidates above to determine confidence level.

**Confidence guide:**
- Both GitNexus + Roslynator flag → **High** — safe to remove
- Only GitNexus flags → **Medium** — check for event handlers / reflection / interface impls
- Only Roslynator flags → **Low** — verify not used as public API from outside
"@

Add-Content -Path $reportFile -Value $header

Write-Host ""
Write-Host "Done. Review: $reportFile" -ForegroundColor Green
Write-Host "Next step:   .\tools\Remove-DeadCode.ps1 --dry-run" -ForegroundColor Yellow
