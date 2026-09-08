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
- Plugin: `src/AutoBIMFusion.Plugin/AutoBIMFusion.Plugin.csproj`, `x64`.
- `Directory.Build.props` selects the framework for all three projects: A19/A20 `net47`, A21–A24 `net48`, A25/A26 .NET 8, A27 .NET 10. Modern desktop targets add `-windows`.
- Configurations: `DebugA19`–`DebugA27`, `ReleaseA19`–`ReleaseA27`. Build with .NET SDK 10.0.300+.
- Package versions are centralized:
  - `AutoCAD.NET`: `$(AcadPackageVersion).*`, except A26 `[25.1.0, 25.1.1)` for .NET 8 compatibility
  - `AutoCAD.NET.Interop`: `$(AcadInteropPackageVersion).*` in A25–A27; unused by legacy configurations
  - `Serilog`: `4.0.0`
  - `Serilog.Sinks.File`: `6.0.0`
- AutoCAD host assemblies must not be copied to output as runtime assets.

## Version mapping

| AutoCAD | Config suffix | `AcadPackageVersion` | `AcadInteropPackageVersion` | Preprocessor |
|---------|---------------|----------------------|-----------------------------|--------------|
| 2019 | A19 | `23.0` | not referenced | `ACAD2019` |
| 2020 | A20 | `23.1` | not referenced | `ACAD2020` |
| 2021 | A21 | `24.0` | not referenced | `ACAD2021` |
| 2022 | A22 | `24.1` | not referenced | `ACAD2022` |
| 2023 | A23 | `24.2` | not referenced | `ACAD2023` |
| 2024 | A24 | `24.3` | not referenced | `ACAD2024` |
| 2025 | A25 | `25.0` | `2025` | `ACAD2025` |
| 2026 | A26 | `25.1` | `2026.0` | `ACAD2026` |
| 2027 | A27 | `26.0` | `2026.0` | `ACAD2027` |

## Core AutoCAD API rules

- All AutoCAD API calls stay on the main thread.
- Every active-document write requires `using (doc.LockDocument())`.
- Use `TransactionManager.StartTransaction()` for database reads/writes and call `Commit()` explicitly.
- Background DWG reads use `new Database(false, true)`, then `ReadDwgFile(...)`, then `CloseInput(true)`.
- AutoCAD assemblies come from the host installation; package references are compile-time references only.
- `CoreConsoleDiagnostics=true` is an AutoCAD core-console build mode for this repo and excludes Ribbon/WPF code.
