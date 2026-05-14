# AGENTS.md — AutoBIMFusion

AutoCAD .NET plugin for AutoCAD 2025-2027. The plugin project `src/AutoBIMFusion.Plugin/AutoBIMFusion.Plugin.csproj` targets `x64`; A25/A26 configurations target `net8.0`, while A27 targets `net10.0` because `AutoCAD.NET 26.x` does not support `net8.0`.
Civil 3D and Plant 3D are verticals on the same base platform.

---

## Build

Solution uses the **new `.slnx` format** (XML, not legacy `.sln`). `dotnet build` supports it directly.

```powershell
# Pick a config: DebugA25 / DebugA26 / DebugA27 / ReleaseA25 / ReleaseA26 / ReleaseA27
dotnet build AutoBIMFusion.slnx -c DebugA26
dotnet clean AutoBIMFusion.slnx -c DebugA26

# Headless/core-console build (strips Ribbon/WPF for accoreconsole.exe)
dotnet build AutoBIMFusion.slnx -c DebugA26 /p:CoreConsoleDiagnostics=true

# Smoke test
dotnet run --project tests/AutoBIMFusion.Tests/AutoBIMFusion.Tests.csproj -c DebugA26

# Unit tests
dotnet test tests/AutoBIMFusion.Tests/AutoBIMFusion.Tests.csproj -c DebugA26
```

Only `src/AutoBIMFusion.Plugin` creates and deploys the `.bundle` to `%AppData%\Autodesk\ApplicationPlugins\AutoBIMFusion.bundle`.
`dotnet clean` removes it. No manual copy needed.

---

## Multi-version config (`Directory.Build.props`)

| Config suffix | AutoCAD | `AcadPackageVersion` | `AcadInteropPackageVersion` | Preprocessor |
|---|---|---|---|---|
| A25 | 2025 | 25.0 | 2025.0 | `ACAD2025` |
| A26 | 2026 | 25.1 | 2026.0 | `ACAD2026` |
| A27 | 2027 | 26.0 | 2026.0 | `ACAD2027` |

NuGet versions are centrally managed in `Directory.Packages.props`. `AutoCAD.NET` floats as `$(AcadPackageVersion).*`; `AutoCAD.NET.Interop` floats as `$(AcadInteropPackageVersion).*`; `Serilog` is fixed at `4.0.0`; `Serilog.Sinks.File` is fixed at `6.0.0`. Do not pin AutoCAD package versions manually.

---

## Running / testing

- **Auto-load:** After build, launch AutoCAD — the plugin loads automatically from `%AppData%\Autodesk\ApplicationPlugins\`.
- **Manual load:** AutoCAD command line → `NETLOAD` → select `AutoBIMFusion.dll`.
- **Unit Tests:**
  - Projects: `tests/AutoBIMFusion.Tests` targets `xUnit`.
  - Execution: `dotnet test tests/AutoBIMFusion.Tests/AutoBIMFusion.Tests.csproj -c DebugA26`.
  - Adding tests: Follow `xUnit` patterns. Tests for `AutoBIMFusion.Merge` internals are supported via `InternalsVisibleTo` in `src/AutoBIMFusion.Merge/AssemblyInfo.cs`.
- **Headless diagnostic test:**

```powershell
.\tools\Run-MergeDwgDiagTest.ps1
.\tools\Run-MergeDwgDiagTest.ps1 -Configuration DebugA27 -AutoCADRoot "C:\Program Files\Autodesk\AutoCAD 2027"
.\tools\Run-MergeDwgDiagTest.ps1 -SkipBuild
```

This script builds a local core-console bundle and then tries to run `MERGEDWG_DIAG_TEST` via `accoreconsole.exe`.
Current code does not register `[CommandMethod("MERGEDWG_DIAG_TEST")]`; treat the script as a known broken diagnostic helper until the command is restored or the script is updated. Do not use it as an acceptance gate.

There is **no CI pipeline**.

---

## Hard constraints (never violate)

- Public autoload names stay stable: `AutoBIMFusion.bundle`, `AutoBIMFusion.dll`, `./Contents/AutoBIMFusion.dll` in `PackageContents.xml`.
- Never copy host DLLs to output. AutoCAD/Civil/Plant assemblies must use `ExcludeAssets="runtime"` (NuGet) or `<Private>false</Private>` (direct refs).
- All AutoCAD API calls must stay on the main thread. The API is not thread-safe.
- **`DocumentLock` required for every write:** `using (doc.LockDocument()) { ... }`
- Entry points auto-registered via `[assembly: ExtensionApplication]` and `[assembly: CommandClass]` — no manual registration.
- `MERGEDWG` stays in the plugin assembly so AutoCAD discovers it reliably.
- Core-console/headless builds must compile without Ribbon/WPF code.
- `CoreConsoleDiagnostics=true` excludes `AutoBIMFusionExtension.cs`, all `Ribbon/` code, and the WPF FrameworkReference from compilation.

---

## Architecture

Multi-project solution:

```text
src/
├── AutoBIMFusion.Plugin/
│   ├── AutoBIMFusionExtension.cs      ← IExtensionApplication entry point
│   ├── Commands/                      ← active command: MERGEDWG
│   │   └── Archive/                   ← excluded commands
│   ├── Ribbon/                        ← excluded when CoreConsoleDiagnostics=true
│   └── Resources/
├── AutoBIMFusion.Merge/
│   └── Combine/                       ← CombineOrchestrator, BlockInserter, layouts, dimensions, optimizer
├── AutoBIMFusion.Common/
│   ├── AcadSupport/                   ← AutoCAD system-variable and unit scopes
│   └── LayoutUtil, FileUtil, UiDialogService
└── AutoBIMFusion.Infrastructure/
    └── Logging/                       ← Serilog wiring

tests/
└── AutoBIMFusion.Tests/               ← xUnit tests & executable smoke-test

docs/                                  ← repo-level documentation
```

Dependency direction:

```text
AutoBIMFusion.Plugin -> AutoBIMFusion.Merge -> AutoBIMFusion.Common
AutoBIMFusion.Plugin -> AutoBIMFusion.Infrastructure
AutoBIMFusion.Merge  -> AutoBIMFusion.Common
AutoBIMFusion.Tests  -> AutoBIMFusion.Merge
```

High-blast-radius classes by project:

- `src/AutoBIMFusion.Plugin/Commands/CombineCommands.cs`
- `src/AutoBIMFusion.Merge/Combine/CombineOrchestrator.cs`
- `src/AutoBIMFusion.Merge/Combine/BlockInserter.cs`
- `src/AutoBIMFusion.Merge/Combine/Layouts/LayoutProjectionProcessor.cs`
- `src/AutoBIMFusion.Merge/Combine/Layouts/ViewportTransformer.cs`
- `src/AutoBIMFusion.Merge/Combine/Layouts/DimensionStyleNormalizer.cs`
- `src/AutoBIMFusion.Merge/Combine/Layouts/DimensionStyleDiagnosticUtils.cs`
- `src/AutoBIMFusion.Merge/Combine/Layouts/ExtentsUtils.cs`
- `src/AutoBIMFusion.Infrastructure/Logging/LoggerFactory.cs`

Archived command classes are excluded from builds but remain under `src/AutoBIMFusion.Plugin/Commands/Archive`.

---

## Public module boundaries

Keep public surface area narrow. Intended cross-project entry points are:

- `AutoBIMFusion.Merge.CombineOrchestrator.MergeSingleFile(...)`
- `AutoBIMFusion.Merge.BlockInserter`
- `AutoBIMFusion.Merge.CombineStatistics`
- `AutoBIMFusion.Merge.CombineResult`
- `AutoBIMFusion.Merge.RasterImagePathFixer`
- `AutoBIMFusion.Merge.DwgOptimizer`
- `AutoBIMFusion.Infrastructure.Logging.LoggerFactory`
- required helpers in `AutoBIMFusion.Common`

Layout internals should remain `internal` unless plugin orchestration requires a public diagnostic hook. `AutoBIMFusion.Merge` exposes internals to `AutoBIMFusion.Tests`.

---

## Skills

- **Code Style:** Follow existing patterns. Use `internal` for logic not required by the Plugin. Use `using` scopes from `AutoBIMFusion.Common.AcadSupport` for AutoCAD state management (System variables, units).
- **AutoCAD API:** Strictly main-thread only. Always use `DocumentLock` for database modifications.

Agentic skill guides are in `skills/`:

| File | Use when |
|---|---|
| `skills/scaffold.md` | Creating a new AutoCAD/Civil 3D/Plant 3D plugin project |
| `skills/autocad-api.md` | Choosing Core vs Full NuGet package; official API docs links |
| `skills/code-sleuth.md` | `rg` patterns for SDK samples; runtime discovery patterns |

---

## Machine-specific env vars (prompt user, never hardcode)

| Variable | Default | Notes |
|---|---|---|
| `ACAD_HOME` | `C:\Program Files\Autodesk\AutoCAD 2027` | Used by scaffold and debugger paths |
| `PLANT_SDK` | *(none)* | Required before generating Plant 3D projects |

---

## Prerequisites

```powershell
dotnet --version       # must be present
rg --version           # ripgrep — install: winget install BurntSushi.ripgrep.MSVC
```

<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **AutoBIMFusion** (2710 symbols, 5599 relationships, 228 execution flows). Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.

> If any GitNexus tool warns the index is stale, run `npx gitnexus analyze` in terminal first.

## Always Do

- **MUST run impact analysis before editing any symbol.** Before modifying a function, class, or method, run `gitnexus_impact({target: "symbolName", direction: "upstream"})` and report the blast radius (direct callers, affected processes, risk level) to the user.
- **MUST run `gitnexus_detect_changes()` before committing** to verify your changes only affect expected symbols and execution flows.
- **MUST warn the user** if impact analysis returns HIGH or CRITICAL risk before proceeding with edits.
- When exploring unfamiliar code, use `gitnexus_query({query: "concept"})` to find execution flows instead of grepping. It returns process-grouped results ranked by relevance.
- When you need full context on a specific symbol — callers, callees, which execution flows it participates in — use `gitnexus_context({name: "symbolName"})`.

## Never Do

- NEVER edit a function, class, or method without first running `gitnexus_impact` on it.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis.
- NEVER rename symbols with find-and-replace — use `gitnexus_rename` which understands the call graph.
- NEVER commit changes without running `gitnexus_detect_changes()` to check affected scope.

## Resources

| Resource | Use for |
|----------|---------|
| `gitnexus://repo/AutoBIMFusion/context` | Codebase overview, check index freshness |
| `gitnexus://repo/AutoBIMFusion/clusters` | All functional areas |
| `gitnexus://repo/AutoBIMFusion/processes` | All execution flows |
| `gitnexus://repo/AutoBIMFusion/process/{name}` | Step-by-step execution trace |

## CLI

| Task | Read this skill file |
|------|---------------------|
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->
