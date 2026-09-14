# AGENTS.md — AutoBIMFusion

AutoCAD .NET plugin for AutoCAD 2019-2027. The plugin project `src/AutoBIMFusion.Plugin/AutoBIMFusion.Plugin.csproj` targets `x64`; A19/A20 target `net47`, A21–A24 target `net48`; A25/A26 configurations target `net8.0`, while A27 targets `net10.0` because `AutoCAD.NET 26.x` does not support `net8.0`.
Civil 3D and Plant 3D are verticals on the same base platform.

---

## Build

Solution uses the **new `.slnx` format** (XML, not legacy `.sln`). `dotnet build` supports it directly.

```powershell
# Pick DebugA19–DebugA27 or ReleaseA19–ReleaseA27; use .NET SDK 10.0.300+
dotnet build AutoBIMFusion.slnx -c DebugA26
dotnet clean AutoBIMFusion.slnx -c DebugA26

# Headless/core-console build (strips Ribbon/WPF for accoreconsole.exe)
dotnet build AutoBIMFusion.slnx -c DebugA26 /p:CoreConsoleDiagnostics=true
```

Only `src/AutoBIMFusion.Plugin` creates and deploys the `.bundle` to `%AppData%\Autodesk\ApplicationPlugins\AutoBIMFusion.bundle`.
Desktop `dotnet clean` removes it. Headless builds/cleans do not deploy or remove the desktop installation.
`DisableAutoCADDeployment=true` skips installation but still creates the local bundle.
Output and intermediate files are separated by desktop/headless mode for every framework.
Both automatic and manual installation use `tools/Publish-AutoCADBundle.ps1`: stage and verify before replacement, retain a backup for rollback. Do not delete installed contents before copying.

---

## Multi-version config (`Directory.Build.props`)

| Config suffix | AutoCAD | `AcadPackageVersion` | `AcadInteropPackageVersion` | Preprocessor |
|---|---|---|---|---|
| A19 | 2019 | 23.0 | not referenced | `ACAD2019` |
| A20 | 2020 | 23.1 | not referenced | `ACAD2020` |
| A21 | 2021 | 24.0 | not referenced | `ACAD2021` |
| A22 | 2022 | 24.1 | not referenced | `ACAD2022` |
| A23 | 2023 | 24.2 | not referenced | `ACAD2023` |
| A24 | 2024 | 24.3 | not referenced | `ACAD2024` |
| A25 | 2025 | 25.0 | 2025 | `ACAD2025` |
| A26 | 2026 | 25.1 | 2026.0 | `ACAD2026` |
| A27 | 2027 | 26.0 | 2026.0 | `ACAD2027` |

NuGet versions are centrally managed in `Directory.Packages.props`. `AutoCAD.NET` floats as `$(AcadPackageVersion).*`; `AutoCAD.NET.Interop` floats as `$(AcadInteropPackageVersion).*`; `Serilog` is fixed at `4.0.0`; `Serilog.Sinks.File` is fixed at `6.0.0`. A26 retains the range `[25.1.0, 25.1.1)` because later packages require .NET 10. Legacy builds omit unused COM interop references. Do not pin AutoCAD package versions manually.

---

## Running / testing

- **Auto-load:** After build, launch AutoCAD — the plugin loads automatically from `%AppData%\Autodesk\ApplicationPlugins\`.
- **Manual load:** AutoCAD command line → `NETLOAD` → select `AutoBIMFusion.dll`.

Compatibility checks: `tools/Test-AutoCADBuildMatrix.ps1` builds all 36 variants and verifies bundles in `out/compatibility`; `dotnet run --project tests/AutoBIMFusion.Compatibility.Tests -c DebugA19` runs host-independent legacy tests. Also test A24, A26 and A27.
Use `tools/Test-AutoCADHost.ps1 -Configuration DebugA19 -AutoCADRoot $env:ACAD_HOME` for a generated-DWG smoke test in an installed host. Match configuration and host version.
The host test uses `/isolate` and verifies a log record containing the unique run folder; do not change the real AutoCAD profile for tests.
`tools/Test-AutoCADBundlePublication.ps1` verifies replacement and installation failure handling in temporary workspace folders.
Legacy BCL dependency binding uses `LegacyDependencyResolver`; never rely on plugin DLL.config redirects or modify host executable configuration.
Core Console requires an open, empty, unnamed drawing; never compile `DocumentCollection.Add` into its document-selection path because it loads desktop modules.
The installed bundle targets one selected year and is replaced by subsequent builds.
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
│   ├── Commands/                      ← active commands: MERGEDWG, QUICKPDF
│   ├── Ribbon/                        ← excluded when CoreConsoleDiagnostics=true
│   └── Resources/
├── AutoBIMFusion.Merge/
│   └── Combine/                       ← CombineOrchestrator, BlockInserter, layouts, dimensions, optimizer
├── AutoBIMFusion.Common/
│   ├── AcadSupport/                   ← AutoCAD system-variable and unit scopes
│   ├── Extensions/                    ← AutoCAD API extension methods used by merge/plot
│   └── Logging/                       ← Serilog wiring (including LoggerFactory)

docs/                                  ← repo-level documentation
```

Dependency direction:

```text
AutoBIMFusion.Plugin -> AutoBIMFusion.Merge -> AutoBIMFusion.Common
```

High-blast-radius classes by project:

- `src/AutoBIMFusion.Plugin/Commands/CombineCommands.cs`
- `src/AutoBIMFusion.Merge/Combine/CombineOrchestrator.cs`
- `src/AutoBIMFusion.Merge/Combine/BlockInserter.cs`
- `src/AutoBIMFusion.Merge/Combine/Layouts/LayoutProjectionProcessor.cs`
- `src/AutoBIMFusion.Merge/Combine/Layouts/ViewportTransformer.cs`
- `src/AutoBIMFusion.Merge/Combine/Layouts/DimensionStyleNormalizer.cs`
- `src/AutoBIMFusion.Common/Helpers/ExtentsUtils.cs`
- `src/AutoBIMFusion.Common/Logging/LoggerFactory.cs`

---

## Public module boundaries

Keep public surface area narrow. Intended cross-project entry points are:

- `AutoBIMFusion.Merge.CombineOrchestrator.MergeSingleFile(...)`
- `AutoBIMFusion.Merge.BlockInserter`
- `AutoBIMFusion.Merge.CombineStatistics`
- `AutoBIMFusion.Merge.CombineResult`
- `AutoBIMFusion.Merge.RasterImagePathFixer`
- `AutoBIMFusion.Merge.DrawingPurger`
- `AutoBIMFusion.Common.Logging.LoggerFactory`
- required helpers in `AutoBIMFusion.Common`

Layout internals should remain `internal` unless plugin orchestration requires a public diagnostic hook.

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

This project is indexed by GitNexus as **AutoBIMFusion** (928 symbols, 2206 relationships, 74 execution flows).

> Index stale? Run `node .gitnexus/run.cjs analyze --index-only` from the project root — it auto-selects an available runner. No `.gitnexus/run.cjs` yet? Bootstrap with `npx`, `bunx`, or `pnpm dlx` — e.g. `bunx gitnexus@latest analyze` (npm 11 npx crash; #1939).

## Always Do

- **MUST run impact before editing.** Use `impact({target: "symbolName", direction: "upstream"})` or `node .gitnexus/run.cjs impact "symbolName" --direction upstream --repo .`; report callers, processes, and risk. Never substitute grep for graph analysis.
- **MUST analyze graph changes before committing.** Use `detect_changes({scope: "all"})` (MCP) or `node .gitnexus/run.cjs detect-changes --scope all --repo .` (CLI fallback). `partial: true` or `truncated: true` is not a clean check — a zero means unseen, not unaffected; re-run it. For regression review: `detect_changes({scope: "compare", base_ref: "main"})` or `node .gitnexus/run.cjs detect-changes --scope compare --base-ref "main" --repo .`.
- MUST warn on HIGH/CRITICAL `risk` pre-edit; never use `riskSharedAxes` to waive a HIGH/CRITICAL `risk` warning. Compare File/symbol: MCP File omits axes; Graph-RAG expands File.
- **MUST treat `risk: UNKNOWN` as unresolved, not as low.** An empty caller set is not evidence the symbol is unused — it can also mean the callers are not resolvable by the index (plain-object property access, dynamic dispatch, cross-language calls). `impact` pairs `UNKNOWN` with a `riskNote` saying so. Confirm with a text search before treating the symbol as safe to change or delete; do not proceed on the strength of a zero.
- **MUST use `query({search_query: "concept"})` for concepts/flows, `context({name: "symbolName"})` for a named symbol, or `impact` for blast radius, on read-only callers, dependencies, imports, or execution flow.** Graph first; text search only for empty/`UNKNOWN`/literals.
- For security review, `explain({target: "fileOrSymbol"})` lists taint findings (source→sink flows; needs `analyze --pdg`).

## Never Do

- NEVER edit a function, class, or method before MCP/CLI impact analysis.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis, and never read `UNKNOWN` as an all-clear — it means the walk could not answer, which is the one verdict that requires confirming by other means.
- NEVER rename symbols with find-and-replace — use `rename` which understands the call graph.
- NEVER commit before MCP/CLI graph change analysis.

## Resources

| Resource | Use for |
| --- | --- |
| `gitnexus://repo/AutoBIMFusion/context` | Codebase overview, check index freshness |
| `gitnexus://repo/AutoBIMFusion/clusters` | All functional areas |
| `gitnexus://repo/AutoBIMFusion/processes` | All execution flows |
| `gitnexus://repo/AutoBIMFusion/process/{name}` | Step-by-step execution trace |

## CLI

| Task | Read this skill file |
| --- | --- |
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->
