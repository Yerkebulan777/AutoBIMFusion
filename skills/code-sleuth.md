# Code Sleuth — AutoCAD .NET Search Patterns

AutoCAD-only search guide for this repository. Use it only for desktop AutoCAD plugin work and AutoCAD SDK/API lookup.

## Ripgrep baseline

Verify `rg` exists:

```powershell
rg --version
```

If missing:

```powershell
winget install BurntSushi.ripgrep.MSVC
```

## AutoBIMFusion repo checks

```powershell
# Compiled AutoCAD commands
rg -n "\[CommandMethod" src --glob "*.cs"

# Command classes and AutoCAD entry points
rg -n "CommandMethod|IExtensionApplication|ExtensionApplication|CommandClass" src --glob "*.cs"

# DocumentLock and transaction usage
rg -n "LockDocument|StartTransaction|Commit\(\)" src --glob "*.cs"

# Bundle deployment and core-console exclusions
rg -n "CoreConsoleDiagnostics|CreateAutoCADBundle|CleanAutoCADBundle|ApplicationPlugins|BundleName" src/AutoBIMFusion.Plugin Directory.Build.props Directory.Build.targets

# AutoCAD package and version mapping
rg -n "TargetFramework|AcadPackageVersion|AcadInteropPackageVersion|PackageVersion|DefineConstants" Directory.Build.props Directory.Packages.props src
```

Current compiled commands are `MERGEDWG` and `QUICKPDF`.

## AutoCAD API lookup

Use official AutoCAD docs and NuGet XML docs first:

```powershell
# Find AutoCAD type references in package XML docs
rg "Database" "$env:USERPROFILE\.nuget\packages\autocad.net" --glob "*.xml"

# Find command patterns in this repo
rg -n "\[CommandMethod" src --glob "*.cs"

# Find entity type checks
rg -n " is (DBText|MText|Line|Dimension|Entity)|DxfName" src --glob "*.cs"

# Find system variable scopes
rg -n "AcadWarningSuppressScope|Application\\.SetSystemVariable|GetSystemVariable" src --glob "*.cs"
```

## Runtime discovery inside AutoCAD

When docs are sparse, inspect live AutoCAD objects inside a temporary command:

```csharp
Entity ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
ed.WriteMessage($"\nType: {ent.GetType().FullName}; DXF: {id.ObjectClass.DxfName}; Assembly: {ent.GetType().Assembly.GetName().Name}");
```

Keep discovery commands local or remove them before committing unless they are intentionally productized diagnostics.
