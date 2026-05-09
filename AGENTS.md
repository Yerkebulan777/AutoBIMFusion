# AGENTS.md — AutoBIMFusion

AutoCAD .NET plugin for AutoCAD 2025-2027. `AutoBIMFusion/AutoBIMFusion.csproj` targets `net8.0`, `x64`; `Directory.Build.props` contains a shared `net10.0-windows` value, but the project overrides it.
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
```

Every build deploys the `.bundle` to `%AppData%\Autodesk\ApplicationPlugins\AutoBIMFusion.bundle`.
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
- **Manual load:** AutoCAD command line → `NETLOAD` → select the built DLL.
- **Headless diagnostic test:**

```powershell
.\tools\Run-MergeDwgDiagTest.ps1
.\tools\Run-MergeDwgDiagTest.ps1 -Configuration DebugA27 -AutoCADRoot "C:\Program Files\Autodesk\AutoCAD 2027"
.\tools\Run-MergeDwgDiagTest.ps1 -SkipBuild
```

This script builds a local core-console bundle and then tries to run `MERGEDWG_DIAG_TEST` via `accoreconsole.exe`. Logs are in `AutoBIMFusion\bin\DebugA26-core\diag\`.
Current code does not register `[CommandMethod("MERGEDWG_DIAG_TEST")]`; treat the script as a known broken diagnostic helper until the command is restored or the script is updated. Do not use it as an acceptance gate.

There is **no CI pipeline**.

---

## Hard constraints (never violate)

- Never copy host DLLs to output. AutoCAD/Civil/Plant assemblies must use `ExcludeAssets="runtime"` (NuGet) or `<Private>false</Private>` (direct refs).
- All AutoCAD API calls must stay on the main thread. The API is not thread-safe.
- **`DocumentLock` required for every write:** `using (doc.LockDocument()) { ... }`
- Entry points auto-registered via `[assembly: ExtensionApplication]` and `[assembly: CommandClass]` — no manual registration.
- Core-console/headless builds must compile without Ribbon/WPF code.
- `CoreConsoleDiagnostics=true` excludes `AutoBIMFusionExtension.cs`, all `Ribbon/` code, and the WPF FrameworkReference from compilation.

---

## Architecture

Single project: `AutoBIMFusion/AutoBIMFusion.csproj`

```
AutoBIMFusion/
├── Application/
│   ├── AutoBIMFusionExtension.cs   ← IExtensionApplication entry point
│   ├── Commands/                   ← active command: MERGEDWG
│   │   └── Archive/                ← excluded commands: SMART_MERGE_TEXT, MERGE_TEXT_STYLES, JOIN_LINES, CREATE_ETRANSMIT_ZIP
│   ├── Combine/                    ← CombineOrchestrator, BlockInserter, DwgOptimizer, …
│   ├── Ribbon/                     ← excluded when CoreConsoleDiagnostics=true
│   └── Utils/
├── Infrastructure/Logging/         ← Serilog wiring (AILog is most-connected class)
└── docs/                           ← TECHNICAL_DOCUMENTATION.md, ALGORITHM.md, KNOWN_ISSUES.md
```

High-blast-radius classes:
`DimensionStyleDiagnosticUtils`, `DimensionStyleNormalizer`, `ExtentsUtils`, `ViewportTransformer`, `CombineCommands`, `LayoutProjectionProcessor`, `LoggerFactory`.

Archived command classes are excluded from builds but still exist under `Application/Commands/Archive`.

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
