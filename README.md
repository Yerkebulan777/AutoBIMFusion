# AutoBIMFusion

AutoBIMFusion is a .NET 8 plugin for AutoCAD 2025-2027. Its main command, `MERGEDWG`, merges DWG files from a selected folder into the active drawing while keeping the imported geometry editable as native Model Space entities.

## Commands

| Command | Purpose |
| :--- | :--- |
| `MERGEDWG` | Recursively finds DWG files, exports the first Paper Space layout of each file, and inserts the result into the active drawing. |
| `MERGEDWG_DIAG_TEST` | Diagnostic-only merge run for `C:\Users\y.zhumabayev\Desktop\TEST`, without folder picker or final summary dialog. |
| `SMART_MERGE_TEXT` | Groups nearby `TEXT` / `MTEXT` objects in Model Space and replaces each group with one `MText`. |
| `MergeTextStyles` | Finds duplicate text styles, reassigns text and attributes to a master style, then removes duplicates. |
| `CreateETransmitZip` | Creates an AutoCAD eTransmit package for the current saved drawing and writes a ZIP archive. |
| `ExportTextStylesToMd` / `ExportDimStylesToMd` | Exports style table diagnostics to Markdown on the desktop. |

## Merge Flow

1. `MergeCommands` guards against parallel `MERGEDWG` runs with `SemaphoreSlim`.
2. `FileEnumerator` collects DWG files up to 3 folder levels deep, skipping names prefixed with `#` and files larger than 15 MB.
3. `MergeCoordinator.MergeSingleFile` validates each DWG, exports the first layout via `ViewportLayoutExporter`, then inserts the temporary result via `BlockInserter`.
4. `ViewportLayoutExporter` delegates viewport math and layout projection to `LayoutProjectionProcessor`:
   - 0 viewport: move Paper Space to Model Space with default 1:100 scaling,
   - 1 viewport: clamp scale if needed, scale Model Space once, then project Paper Space,
   - multi viewport: flatten aux viewport content to main viewport coordinates, remove aux-only originals, then project Paper Space.
5. `ModelSpaceTrimmer.TrimOutside` removes objects outside the projected frame bounds.
6. `BlockInserter` clones native entities into the target Model Space with `WblockCloneObjects` and places each sheet along X with a calculated gap.
7. The final drawing normalizes raster file paths next to the target DWG (`RasterImagePathFixer`), purges unused database objects, saves as `AC1032`, then runs `REGENALL` and `ZOOM EXTENTS`.

## Current Design Rules

- AutoCAD transactions and disposable objects are always scoped with `using`.
- Command classes own AutoCAD UI entry points; utility classes contain file, layout, geometry, and logging helpers.
- Geometry helpers stay small and only expose methods used by the merge pipeline.
- Per-entity debug logging is kept limited; high-volume operations log counts and critical failures.
- AutoCAD API exceptions are caught at command/file boundaries and reported through `AILog`.

## Known Tradeoffs

- `DuplicateRecordCloning.Ignore` keeps the target drawing stable but may reuse existing layers/styles instead of preserving conflicting source definitions.
- Only the first Paper Space layout is processed.
- Long merge operations do not yet support cancellation.

## Build

```powershell
dotnet build AutoBIMFusion.slnx -c DebugA26
dotnet build AutoBIMFusion.slnx -c ReleaseA26
```

The MSBuild target creates and deploys an AutoCAD bundle under `%AppData%\Autodesk\ApplicationPlugins\AutoBIMFusion.bundle` by default.

## Diagnostic Merge Run

```powershell
dotnet build AutoBIMFusion.slnx -c DebugA26
.\tools\Run-MergeDwgDiagTest.ps1 -Configuration DebugA26
```

The diagnostic runner builds a Core Console-safe plugin variant and invokes `MERGEDWG_DIAG_TEST` against `C:\Users\y.zhumabayev\Desktop\TEST`. It loads the DLL from `AutoBIMFusion\bin\DebugA26-core\AutoBIMFusion.bundle\Contents`, writes Core Console stdout/stderr to `AutoBIMFusion\bin\DebugA26-core\diag`, and writes plugin logs next to the loaded bundle DLL under `Contents\Logs\merge-YYYY-MM-DD.log`. Merge runs emit two compact style snapshots: `before-merge` and `after-merge`. Each snapshot logs user dimension styles as `[DIM-STYLE]`, user text styles as `[TEXT-STYLE]`, and the final pass logs `[DIM-OVERRIDES]` after removing per-entity dimension style overrides.

## Documentation

- [Technical documentation](AutoBIMFusion/docs/TECHNICAL_DOCUMENTATION.md)
- [Merge algorithm](AutoBIMFusion/docs/ALGORITHM.md)
- [Known issues](AutoBIMFusion/docs/KNOWN_ISSUES.md)
