# AutoBIMFusion

AutoBIMFusion is a .NET 8 plugin for AutoCAD 2025-2027. Its main command, `MERGEDWG`, merges DWG files from a selected folder into the active drawing while keeping the imported geometry editable as native Model Space entities.

## Commands

| Command | Purpose |
| :--- | :--- |
| `MERGEDWG` | Recursively finds DWG files, exports the first Paper Space layout of each file, and inserts the result into the active drawing. |
| `SMART_MERGE_TEXT` | Groups nearby `TEXT` / `MTEXT` objects in Model Space and replaces each group with one `MText`. |
| `MergeTextStyles` | Finds duplicate text styles, reassigns text and attributes to a master style, then removes duplicates. |
| `CreateETransmitZip` | Creates an AutoCAD eTransmit package for the current saved drawing and writes a ZIP archive. |
| `ExportTextStylesToMd` / `ExportDimStylesToMd` | Exports style table diagnostics to Markdown on the desktop. |

## Merge Flow

1. `MergeCommands` guards against parallel `MERGEDWG` runs with `SemaphoreSlim`.
2. `FileEnumerator` collects DWG files up to 3 folder levels deep, skipping names prefixed with `#` and files larger than 15 MB.
3. `MergeCoordinator.MergeSingleFile` validates each DWG, exports the first layout via `ViewportLayoutExporter`, then inserts the temporary result via `BlockInserter`.
4. `ViewportLayoutExporter` handles 0 / 1 / multi-viewport layouts, moves Paper Space content into Model Space, trims objects outside the frame, and embeds eligible model rasters as `OLE2FRAME`.
5. `BlockInserter` clones native entities into the target Model Space with `WblockCloneObjects` and places each sheet along X with a calculated gap.
6. The final drawing copies remaining raster files next to the target DWG, purges unused database objects, saves as `AC1032`, then runs `REGENALL` and `ZOOM EXTENTS`.

## Current Design Rules

- AutoCAD transactions and disposable objects are always scoped with `using`.
- Command classes own AutoCAD UI entry points; utility classes contain file, layout, geometry, and logging helpers.
- Geometry helpers stay small and only expose methods used by the merge pipeline.
- Per-entity debug logging is kept limited; high-volume operations log counts and critical failures.
- AutoCAD API exceptions are caught at command/file boundaries and reported through `OperationLogger`.

## Known Tradeoffs

- `DuplicateRecordCloning.Ignore` keeps the target drawing stable but may reuse existing layers/styles instead of preserving conflicting source definitions.
- Raster embedding still depends on Clipboard + `PASTECLIP`; the plugin now preserves/restores Clipboard data, but direct OLE creation would be more robust.
- Only the first Paper Space layout is processed.
- Long merge operations do not yet support cancellation.

## Build

```powershell
dotnet build AutoBIMFusion.slnx -c DebugA26
dotnet build AutoBIMFusion.slnx -c ReleaseA26
```

The MSBuild target creates and deploys an AutoCAD bundle under `%AppData%\Autodesk\ApplicationPlugins\AutoBIMFusion.bundle` by default.

## Documentation

- [Technical documentation](AutoBIMFusion/docs/TECHNICAL_DOCUMENTATION.md)
- [Merge algorithm](AutoBIMFusion/docs/ALGORITHM.md)
- [Known issues](AutoBIMFusion/docs/KNOWN_ISSUES.md)
