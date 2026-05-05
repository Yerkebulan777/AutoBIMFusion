# Scaffold Skill — AutoCAD .NET Plugins

AutoCAD-only guide for this repository. Use it only for desktop AutoCAD plugin work.

## AutoBIMFusion repository baseline

- Solution: `AutoBIMFusion.slnx`.
- Project: `AutoBIMFusion/AutoBIMFusion.csproj`.
- Target: `net8.0`, `PlatformTarget=x64`.
- Configurations: `DebugA25`, `DebugA26`, `DebugA27`, `ReleaseA25`, `ReleaseA26`, `ReleaseA27`.
- Package versions: central package management in `Directory.Packages.props`; do not pin AutoCAD package versions in the project file.
- Bundle deployment: every build creates and deploys `AutoBIMFusion.bundle` to `%AppData%\Autodesk\ApplicationPlugins\`.
- Core-console diagnostic build: `/p:CoreConsoleDiagnostics=true` excludes `AutoBIMFusionExtension.cs`, `Application/Ribbon/**`, and WPF.

## New AutoCAD command checklist

1. Add the command class or method under `AutoBIMFusion/Application/Commands`.
2. Register the command with `[CommandMethod("COMMAND_NAME", CommandFlags.Modal)]` or the existing flag pattern needed by AutoCAD.
3. For any write to the active drawing, wrap work in `using (doc.LockDocument())`.
4. Use `TransactionManager.StartTransaction()` and commit explicitly.
5. Keep AutoCAD API calls on the main thread.
6. Log through `LoggerFactory.GetSharedLogger()` or the command-specific logger pattern.
7. Add a Ribbon button only when the command should be exposed in the AutoBIMFusion tab.
8. Build with the target AutoCAD config, for example:

```powershell
dotnet build AutoBIMFusion.slnx -c DebugA26
```

## AutoCAD package mapping

| AutoCAD | Config suffix | `AcadPackageVersion` | `AcadInteropPackageVersion` | Preprocessor |
|---|---|---|---|---|
| 2025 | A25 | `25.0` | `2025.0` | `ACAD2025` |
| 2026 | A26 | `25.1` | `2026.0` | `ACAD2026` |
| 2027 | A27 | `26.0` | `2026.0` | `ACAD2027` |

## Manual AutoCAD plugin scaffold

Use this only for a separate AutoCAD-only plugin project, not for modifying AutoBIMFusion itself.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="AutoCAD.NET" Version="26.0.*" ExcludeAssets="runtime" />
    <PackageReference Include="AutoCAD.NET.Interop" Version="2026.0.*" ExcludeAssets="runtime" />
  </ItemGroup>
</Project>
```

Entry point pattern:

```csharp
using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(MyPlugin.App))]
[assembly: CommandClass(typeof(MyPlugin.Commands))]

namespace MyPlugin;

public sealed class App : IExtensionApplication
{
    public void Initialize() { }
    public void Terminate() { }
}
```

Command pattern:

```csharp
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.Runtime;

namespace MyPlugin;

public sealed class Commands
{
    [CommandMethod("MY_COMMAND", CommandFlags.Modal)]
    public void MyCommand()
    {
        Document? doc = Application.DocumentManager.MdiActiveDocument;
        if (doc?.Database is null)
        {
            return;
        }

        using (doc.LockDocument())
        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
        {
            tr.Commit();
        }
    }
}
```

## Load and run

For AutoBIMFusion, build is enough because the bundle deploys automatically. Then launch AutoCAD and run the command.

Manual fallback: AutoCAD command line -> `NETLOAD` -> select the built DLL under `AutoBIMFusion/bin/<Configuration>/`.
