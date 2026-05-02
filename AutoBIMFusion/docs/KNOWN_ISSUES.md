# Known Issues

**Updated:** 2026-05-02

This file tracks only active risks and contentious design decisions. Fixed stale items were removed.

All current AutoCAD command entry points are intentionally retained: `MERGEDWG`, `MERGEDWG_DIAG_TEST`, `SMART_MERGE_TEXT`, `CreateETransmitZip`, `MergeTextStyles`, `JOIN_LINES`, `ExportTextStylesToMd`, and `ExportDimStylesToMd`.

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

**Where:** `EntityTransformUtils`, `DimensionStyleDiagnosticUtils`, `LayoutProjectionProcessor`

`MERGEDWG` no longer applies per-entity dimension compensation during layout flattening, and finalization now removes per-entity dimension style overrides from the `ACAD` xdata `DSTYLE` section. The goal is for dimensions in the merged DWG to rely on shared project styles instead of local overrides.

**Risk:** dimensions may change text height, arrow size, extension-line geometry, or displayed numeric value after model clamp scaling, Paper Space cloning, or auxiliary viewport flattening.

**Status:** active diagnostics log new per-file source styles as `before-merge`, the final target style snapshot as `after-merge`, and `[DIM-OVERRIDES]` warnings only for concrete dimension override cleanup failures.

**Residual verification:** manual visual QA in AutoCAD is still required for representative production sheets, because full dimension extents may include scaled extension-line geometry even when text and arrow visual metrics are preserved.

**Diagnostic scenarios:** verify one viewport at 1:50, one viewport at 1:200, a Model Space dimension visible through a viewport, a Paper Space dimension, and a dimension in an auxiliary viewport. The merged output must preserve the original layout appearance: displayed value, dimension text height, arrows, and extension lines.

## Recently Fixed

| Item | Fix |
| :--- | :--- |
| Aux viewport double-scaling drift | Aux-to-main transform now uses original main viewport scale before global clamp scaling. |
| Dimension property auto-scaling ×304.8 on imperial source DWGs | After `CloseInput(true)`, `sourceDb.Insunits` and `sourceDb.Measurement` are now forced to match `targetDb` before `WblockCloneObjects`. Syncing before `CloseInput` was insufficient: AutoCAD restores file-header metadata when the input stream is closed, so the sync must happen strictly after `CloseInput`. |
| Aux viewport residual originals | Added `EraseEntitiesOutsideMainWindow` cleanup after aux cloning. |
| Fire-and-forget startup failure | `MergeDwgFolderCommand` catches startup exceptions around the awaited task. |
| ProgressMeter cleanup | `MergeFiles` stops `ProgressMeter` in `finally`. |
| Root repair artifacts | Removed tracked `fix_enc.cs`, `fix_enc.exe`, and `fix.js`. |

## Questions

1. Should source drawing styles/layers be preserved exactly, even if that creates mangled names in the target DWG?
2. Should processing all layouts be added, or should the command intentionally stay first-layout-only?
3. Should operational limits become user-configurable, or remain fixed for predictable behavior?
