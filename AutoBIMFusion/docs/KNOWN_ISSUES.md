# Known Issues

**Updated:** 2026-04-28

This file tracks only active risks and contentious design decisions. Fixed stale items were removed.

## Open

### KI-1. Raster OLE embedding still depends on Clipboard and `PASTECLIP`

**Where:** `Application/Merge/Layouts/ViewportLayoutExporter.cs`

The code now saves and restores the previous Clipboard content, and it erases the source `RasterImage` only after a new `OLE2FRAME` is found. However, the operation still depends on the global Windows Clipboard and AutoCAD command execution.

**Risk:** Another process can change Clipboard contents between `SetDataObject` and `PASTECLIP`, or AutoCAD can reject the paste command in a specific UI state.

**Preferred fix:** Replace Clipboard/command-based insertion with direct OLE API creation if a reliable managed API path is confirmed for supported AutoCAD versions.

### KI-2. `DuplicateRecordCloning.Ignore` can reuse target styles/layers

**Where:** `Application/Merge/BlockInserter.cs`

`WblockCloneObjects` uses `DuplicateRecordCloning.Ignore`. This avoids style/layer duplication and keeps the target database stable, but if a source style has the same name and different properties, cloned objects can inherit the target definition.

**Decision needed:** Keep target-stability behavior, or switch to `MangleName` / explicit style import for visual fidelity.

### KI-3. No cancellation for long merges

**Where:** `MergeCommands`, `MergeCoordinator`, `ViewportLayoutExporter`

Large batches can run for a long time without a user cancellation path.

**Preferred fix:** Introduce a cancellation token through the merge pipeline and check it between files and before expensive layout/raster operations.

### KI-4. Hard-coded operational limits

| Limit | Current value | Location |
| :--- | :--- | :--- |
| Max DWG file size | 15 MB | `FileEnumerator` |
| Max folder recursion | 3 | `FileEnumerator` |
| Viewport scale clamp | 1:100 | `ViewportLayoutExporter` |
| OLE image threshold | 5 MB | `ViewportLayoutExporter` |

These are intentionally conservative, but they are not user-configurable.

**Preferred fix:** Add a small options model or config file only if real projects require different thresholds.

## Recently Fixed

| Item | Fix |
| :--- | :--- |
| Clipboard data loss | Replaced `Clipboard.Clear()` with save/restore in `finally`. |
| New OLE detection by max handle | Detection now compares Model Space snapshots and only uses handle ordering among new `OLE2FRAME` candidates. |
| Duplicate raster copies | `RasterImagePathFixer` reuses one copied file for repeated source image paths. |
| Fire-and-forget startup failure | `MergeDwgFolderCommand` catches startup exceptions around the awaited task. |
| ProgressMeter cleanup | `MergeFiles` stops `ProgressMeter` in `finally`. |
| Root repair artifacts | Removed tracked `fix_enc.cs`, `fix_enc.exe`, and `fix.js`. |

## Questions

1. Should source drawing styles/layers be preserved exactly, even if that creates mangled names in the target DWG?
2. Should the plugin keep converting rasters to OLE, or should large/unstable images remain as external raster references?
3. Should all layouts be processed, or should the command intentionally stay first-layout-only?
