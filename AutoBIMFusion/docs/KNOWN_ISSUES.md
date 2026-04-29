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
