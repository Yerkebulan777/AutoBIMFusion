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
# Compiled AutoCAD commands, excluding archived commands
rg -n "\[CommandMethod" AutoBIMFusion/Application/Commands --glob "!**/Archive/**"

# Command classes and AutoCAD entry points
rg -n "CommandMethod|IExtensionApplication|ExtensionApplication|CommandClass" AutoBIMFusion

# DocumentLock and transaction usage
rg -n "LockDocument|StartTransaction|Commit\(\)" AutoBIMFusion/Application

# Bundle deployment and core-console exclusions
rg -n "CoreConsoleDiagnostics|CreateAutoCADBundle|CleanAutoCADBundle|ApplicationPlugins|BundleName" AutoBIMFusion/AutoBIMFusion.csproj

# AutoCAD package and version mapping
rg -n "TargetFramework|AcadPackageVersion|AcadInteropPackageVersion|PackageVersion|DefineConstants" AutoBIMFusion/AutoBIMFusion.csproj Directory.Build.props Directory.Packages.props

# Stale documentation names without matching this command text literally
rg -n "Merge[C]ommands|Merge[O]rchestrator|Create[E]TransmitZip|Merge[T]extStyles|MERGEDWG_DIAG_TEST" README.md AGENTS.md AutoBIMFusion/docs skills
```

Current compiled command is `MERGEDWG`.

Archived commands `SMART_MERGE_TEXT`, `MERGE_TEXT_STYLES`, `JOIN_LINES`, and `CREATE_ETRANSMIT_ZIP` are under `AutoBIMFusion/Application/Commands/Archive` and excluded by `AutoBIMFusion.csproj`.

`tools/Run-MergeDwgDiagTest.ps1` references `MERGEDWG_DIAG_TEST`, but that command is not registered in the current C# sources.

## AutoCAD API lookup

Use official AutoCAD docs and NuGet XML docs first:

```powershell
# Find AutoCAD type references in package XML docs
rg "Database" "$env:USERPROFILE\.nuget\packages\autocad.net" --glob "*.xml"

# Find command patterns in this repo, excluding archived commands
rg -n "\[CommandMethod" AutoBIMFusion --glob "*.cs" --glob "!Application/Commands/Archive/**"

# Find entity type checks
rg -n " is (DBText|MText|Line|Dimension|Entity)|DxfName" AutoBIMFusion/Application --glob "*.cs"

# Find system variable scopes
rg -n "AcadWarningSuppressScope|AcadUnitScalingOverrideScope|Application\\.SetSystemVariable|GetSystemVariable" AutoBIMFusion/Application --glob "*.cs"
```

## Runtime discovery inside AutoCAD

When docs are sparse, inspect live AutoCAD objects inside a temporary command:

```csharp
Entity ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
ed.WriteMessage($"\nType: {ent.GetType().FullName}; DXF: {id.ObjectClass.DxfName}; Assembly: {ent.GetType().Assembly.GetName().Name}");
```

Keep discovery commands local or remove them before committing unless they are intentionally productized diagnostics.
