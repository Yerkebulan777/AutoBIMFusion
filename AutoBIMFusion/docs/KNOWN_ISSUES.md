# Known Issues

**Updated:** 2026-05-02

This file tracks only active risks and contentious design decisions. Fixed stale items were removed.

All current AutoCAD command entry points are intentionally retained: `MERGEDWG`, `MERGEDWG_DIAG_TEST`, `SMART_MERGE_TEXT`, `CreateETransmitZip`, `MergeTextStyles`, and `JOIN_LINES`.

## Open

### KI-1. `DuplicateRecordCloning.Ignore` can reuse target styles/layers

**Where:** `Application/Merge/BlockInserter.cs`

`WblockCloneObjects` uses `DuplicateRecordCloning.Ignore`. This avoids style/layer duplication and keeps the target database stable, but if a source style has the same name and different properties, cloned objects can inherit the target definition.

**Decision needed:** Keep target-stability behavior, or switch to `MangleName` / explicit style import for visual fidelity.

### KI-2. No cancellation for long merges

**Where:** `MergeCommands`, `MergeCoordinator`, `LayoutProjectionProcessor`

Large batches can run for a long time without a user cancellation path.

**Preferred fix:** Introduce a cancellation token through the merge pipeline and check it between files and before expensive layout operations.

### KI-3. Hard-coded operational limits

| Limit | Current value | Location |
| :--- | :--- | :--- |
| Max DWG file size | 15 MB | `FileEnumerator` |
| Max folder recursion | 3 | `FileEnumerator` |
| Viewport/no-viewport scale clamp | 1:100 | `LayoutProjectionProcessor` |

These are intentionally conservative, but they are not user-configurable.

**Preferred fix:** Add a small options model or config file only if real projects require different thresholds.

### KI-4. Dimension appearance still requires production visual QA

**Severity:** Medium

**Where:** `DimensionStyleNormalizer`, `DimensionStyleDiagnosticUtils`, `LayoutProjectionProcessor`

`MERGEDWG` now normalizes dimensions in the prepared source database before `WblockCloneObjects`. Each Model Space dimension is assigned an effective-main-viewport-scale-specific style named `"{Style} - Scale {Multiplier}"`, where the multiplier comes from the clamped main VP used by the projection algorithm. `ACAD`/`DSTYLE` overrides are removed before the style reset. Source styles that already have model-sized visual values are not multiplied a second time. Replaced source dimension styles are purged only when AutoCAD reports them as unused.

**Risk:** dimensions may change text height, arrow size, extension-line geometry, or displayed numeric value after model clamp scaling, Paper Space cloning, or auxiliary viewport flattening.

**Status:** active diagnostics log new per-file source styles as `before-merge`, the final target style snapshot as `after-merge`, and `[DIM-NORMALIZE]` summaries for source-side style creation and override cleanup.

**Residual verification:** manual visual QA in AutoCAD is still required for representative production sheets, because full dimension extents may include scaled extension-line geometry even when text and arrow visual metrics are preserved.

**Diagnostic scenarios:** verify one viewport at 1:50, one viewport at 1:200, a Model Space dimension visible through a viewport, a Paper Space dimension, and a dimension in an auxiliary viewport. The merged output must preserve the original layout appearance: displayed value, dimension text height, arrows, and extension lines.

### KI-5. Redundant I/O and Multiple DWG Reads

**Where:** `MergeCoordinator`, `ViewportLayoutExporter`, `BlockInserter`

Currently, the temporary DWG file created during export is read from disk multiple times (for bounds calculation and final insertion).

**Preferred fix:** Modify `ViewportLayoutExporter` to return the `Database` object and keep it in memory until the merge of that file is complete.

### KI-6. Double-pass on Cloned Objects

**Where:** `BlockInserter.InsertNativeObjects`

Objects are first cloned, then iterated again to apply `TransformBy`. For large datasets, this second pass adds measurable overhead.

**Preferred fix:** Integrate the transformation/displacement logic into the same loop that performs post-cloning entity cleanup.

## Recently Fixed

| Item | Fix |
| :--- | :--- |
| Aux viewport double-scaling drift | Aux-to-main transform now uses original main viewport scale before global clamp scaling. |
| Scattered dimension post-processing | Removed the old post-merge dimension healer; dimensions are normalized once in `DimensionStyleNormalizer` before cross-database cloning. |
| Dimension property auto-scaling ×304.8 on imperial source DWGs | After `CloseInput(true)`, `sourceDb.Insunits` and `sourceDb.Measurement` are now forced to match `targetDb` before `WblockCloneObjects`. Syncing before `CloseInput` was insufficient: AutoCAD restores file-header metadata when the input stream is closed, so the sync must happen strictly after `CloseInput`. |
| Aux viewport residual originals | Added `EraseEntitiesOutsideMainWindow` cleanup after aux cloning. |
| Fire-and-forget startup failure | `MergeDwgFolderCommand` catches startup exceptions around the awaited task. |
| ProgressMeter cleanup | `MergeFiles` stops `ProgressMeter` in `finally`. |
| Root repair artifacts | Removed tracked `fix_enc.cs`, `fix_enc.exe`, and `fix.js`. |

## Questions

1. Should source drawing styles/layers be preserved exactly, even if that creates mangled names in the target DWG?
2. Should processing all layouts be added, or should the command intentionally stay first-layout-only?
3. Should operational limits become user-configurable, or remain fixed for predictable behavior?
