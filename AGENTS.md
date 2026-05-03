# AGENTS.md — AutoBIMFusion

AutoCAD .NET plugin targeting AutoCAD 2025–2027 (`net8.0-windows`, `win-x64`).
Civil 3D and Plant 3D are verticals on the same base platform.

---

## Build

Solution uses the **new `.slnx` format** (XML, not legacy `.sln`). `dotnet build` supports it directly.

```powershell
# Pick a config: DebugA25 / DebugA26 / DebugA27 / ReleaseA25 / ReleaseA26 / ReleaseA27
dotnet build AutoBIMFusion.slnx -c DebugA26
dotnet clean AutoBIMFusion.slnx -c DebugA26

# Headless build (strips Ribbon/WPF for accoreconsole.exe)
dotnet build AutoBIMFusion.slnx -c DebugA26 /p:CoreConsoleDiagnostics=true
```

**Every build auto-deploys** the `.bundle` to `%AppData%\Autodesk\ApplicationPlugins\AutoBIMFusion.bundle`.
`dotnet clean` removes it. No manual copy needed.

---

## Multi-version config (`Directory.Build.props`)

| Config suffix | AutoCAD | `AcadPackageVersion` | Preprocessor |
|---|---|---|---|
| A25 | 2025 | 25.0 | `ACAD2025` |
| A26 | 2026 | 25.1 | `ACAD2026` |
| A27 | 2027 | 26.0 | `ACAD2027` |

NuGet versions are floating (`$(AcadPackageVersion).*`) via Central Package Management (`Directory.Packages.props`). Do not pin them manually.

---

## Running / testing

- **Auto-load:** After build, launch AutoCAD — the plugin loads automatically from `%AppData%\Autodesk\ApplicationPlugins\`.
- **Manual load:** AutoCAD command line → `NETLOAD` → select the built DLL.
- **Headless diagnostic test:**

```powershell
.\tools\Run-MergeDwgDiagTest.ps1
.\tools\Run-MergeDwgDiagTest.ps1 -Configuration DebugA27 -AutoCADRoot "C:\Program Files\Autodesk\AutoCAD 2027"
.\tools\Run-MergeDwgDiagTest.ps1 -SkipBuild
```

This runs `MERGEDWG_DIAG_TEST` via `accoreconsole.exe`. Logs in `AutoBIMFusion\bin\DebugA26-core\diag\`.
Note: `MERGEDWG_DIAG_TEST` has a **hardcoded test path** (`C:\Users\y.zhumabayev\Desktop\TEST`) — not configurable.

There is **no CI pipeline**.

---

## Hard constraints (never violate)

- **Never copy host DLLs to output.** AutoCAD/Civil/Plant assemblies must use `ExcludeAssets="runtime"` (NuGet) or `<Private>false</Private>` (direct refs).
- **All AutoCAD API calls on the main thread.** The API is not thread-safe.
- **`DocumentLock` required for every write:** `using (doc.LockDocument()) { ... }`
- Entry points auto-registered via `[assembly: ExtensionApplication]` and `[assembly: CommandClass]` — no manual registration.
- Design Automation (headless) must use `AutoCAD.NET.Core` only — no WPF/WinForms.
- `CoreConsoleDiagnostics=true` excludes `AutoBIMFusionExtension.cs`, all `Ribbon/` code, and the WPF FrameworkReference from compilation.

---

## Architecture

Single project: `AutoBIMFusion/AutoBIMFusion.csproj`

```
AutoBIMFusion/
├── Application/
│   ├── AutoBIMFusionExtension.cs   ← IExtensionApplication entry point
│   ├── Commands/                   ← MERGEDWG, SMART_MERGE_TEXT, JOIN_LINES, etc.
│   ├── Merge/                      ← MergeOrchestrator, BlockInserter, DwgOptimizer, …
│   ├── Ribbon/                     ← excluded when CoreConsoleDiagnostics=true
│   └── Utils/
├── Infrastructure/Logging/         ← Serilog wiring (AILog is most-connected class)
└── docs/                           ← TECHNICAL_DOCUMENTATION.md, ALGORITHM.md, KNOWN_ISSUES.md
```

**Most-connected classes** (high blast radius — be careful):
`DimensionStyleDiagnosticUtils`, `DimensionHealer`, `SmartTextCommands`, `ExtentsUtils`,
`TransmittalCommands`, `ViewportTransformer`, `MergeCommands`, `LayoutProjectionProcessor`, `AILog`

---

## Knowledge graph

A pre-built graph (285 nodes, 528 edges) lives at `graphify-out/`.

- Before architectural work, read `graphify-out/GRAPH_REPORT.md` (or navigate `graphify-out/wiki/index.md` if present).
- After modifying code files, run `graphify update .` to keep the graph current (AST-only, no LLM cost).

---

## Skills

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
