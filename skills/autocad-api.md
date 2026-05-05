# AutoCAD .NET API — References

AutoCAD-only reference for this repository. Use it only for desktop AutoCAD plugin work.

## Official docs

| Resource | Link |
|----------|------|
| AutoCAD 2027 Developer's Guide | https://help.autodesk.com/view/OARX/2027/ENU/ |
| AutoCAD 2027 Managed .NET API Reference | https://help.autodesk.com/view/OARX/2027/ENU/?guid=GUID-B1C7E6C8-C90E-4E55-BEB7-B8D08FFE8B21 |
| NuGet: `AutoCAD.NET` | https://www.nuget.org/packages/AutoCAD.NET |
| NuGet: `AutoCAD.NET.Interop` | https://www.nuget.org/packages/AutoCAD.NET.Interop |

## AutoBIMFusion repo facts

- Solution format: `AutoBIMFusion.slnx`.
- Project target: `AutoBIMFusion/AutoBIMFusion.csproj` sets `TargetFramework` to `net8.0` and `PlatformTarget` to `x64`.
- Shared props: `Directory.Build.props` contains `net10.0-windows`, but the project overrides it.
- Configurations: `DebugA25`, `DebugA26`, `DebugA27`, `ReleaseA25`, `ReleaseA26`, `ReleaseA27`.
- Package versions are centralized:
  - `AutoCAD.NET`: `$(AcadPackageVersion).*`
  - `AutoCAD.NET.Interop`: `$(AcadInteropPackageVersion).*`
  - `Serilog`: `4.0.0`
  - `Serilog.Sinks.File`: `6.0.0`
- AutoCAD host assemblies must not be copied to output as runtime assets.

## Version mapping

| AutoCAD | Config suffix | `AcadPackageVersion` | `AcadInteropPackageVersion` | Preprocessor |
|---------|---------------|----------------------|-----------------------------|--------------|
| 2025 | A25 | `25.0` | `2025.0` | `ACAD2025` |
| 2026 | A26 | `25.1` | `2026.0` | `ACAD2026` |
| 2027 | A27 | `26.0` | `2026.0` | `ACAD2027` |

## Core AutoCAD API rules

- All AutoCAD API calls stay on the main thread.
- Every active-document write requires `using (doc.LockDocument())`.
- Use `TransactionManager.StartTransaction()` for database reads/writes and call `Commit()` explicitly.
- Background DWG reads use `new Database(false, true)`, then `ReadDwgFile(...)`, then `CloseInput(true)`.
- AutoCAD assemblies come from the host installation; package references are compile-time references only.
- `CoreConsoleDiagnostics=true` is an AutoCAD core-console build mode for this repo and excludes Ribbon/WPF code.
