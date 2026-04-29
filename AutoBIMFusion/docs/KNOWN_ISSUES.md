# Known Issues

**Updated:** 2026-04-29

This file tracks only active risks and contentious design decisions. Fixed stale items were removed.

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

### KI-4. Critical: dimension text scaling logic may distort merged layouts

**Severity:** Critical

**Where:** `EntityTransformUtils`, `ViewportTransformer.ScaleModelSpaceObjects`, `LayoutProjectionProcessor`

`MERGEDWG` previously applied generic dimension compensation during layout flattening: `Dimension.Dimscale` was multiplied by the transform scale factor and `Dimension.Dimlfac` was divided by the same factor. Diagnostics confirmed that this mixed two different goals: preserving the visual size of dimension text/arrows and preserving the displayed dimension value after viewport/model transformations.

**Risk:** dimensions may change text height, arrow size, extension-line geometry, or displayed numeric value after model clamp scaling, Paper Space cloning, or auxiliary viewport flattening.

**Status:** root cause confirmed and first fix applied. `Dimscale` is no longer multiplied by the geometric transform scale; `Dimlfac` remains the numeric-value compensation. Dimension diagnostic logging is available through `[DIM-DIAG]` debug entries and records scenario, stage, handle, type, scale factor, scale order, measurement, measurement ratio, override text, entity dimension values, linked dimension style values, visual text height, visual arrow size, extents, and bounding-box height. `MERGEDWG_DIAG_TEST` also emits `[DIM-STYLE]` snapshots and `[DIM-DIAG-SUMMARY]` grouped by scenario, scale factor, and stage.

**Residual verification:** manual visual QA in AutoCAD is still required for representative production sheets, because full dimension extents may include scaled extension-line geometry even when text and arrow visual metrics are preserved.

**Diagnostic scenarios:** verify one viewport at 1:50, one viewport at 1:200, a Model Space dimension visible through a viewport, a Paper Space dimension, and a dimension in an auxiliary viewport. The merged output must preserve the original layout appearance: displayed value, dimension text height, arrows, and extension lines.

## Recently Fixed

| Item | Fix |
| :--- | :--- |
| Aux viewport double-scaling drift | Aux-to-main transform now uses original main viewport scale before global clamp scaling. |
| Aux viewport residual originals | Added `EraseEntitiesOutsideMainWindow` cleanup after aux cloning. |
| Fire-and-forget startup failure | `MergeDwgFolderCommand` catches startup exceptions around the awaited task. |
| ProgressMeter cleanup | `MergeFiles` stops `ProgressMeter` in `finally`. |
| Root repair artifacts | Removed tracked `fix_enc.cs`, `fix_enc.exe`, and `fix.js`. |

## Questions

1. Should source drawing styles/layers be preserved exactly, even if that creates mangled names in the target DWG?
2. Should processing all layouts be added, or should the command intentionally stay first-layout-only?
3. Should operational limits become user-configurable, or remain fixed for predictable behavior?
