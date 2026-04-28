# MERGEDWG Algorithm

**Updated:** 2026-04-28
**Primary code:** `Application/Commands/MergeCommands.cs`, `Application/Merge/MergeOrchestrator.cs`, `Application/Merge/BlockInserter.cs`, `Application/Merge/Layouts/*`

This document describes the actual merge path and only the decisions that affect correctness or stability.

## 1. Batch Entry

1. `MERGEDWG` starts in `MergeCommands.MergeDwgFolderCommand`.
2. A `SemaphoreSlim` prevents a second merge from running against the same AutoCAD session.
3. The user selects a source folder.
4. `FileEnumerator.GetFiles` returns sorted DWG files:
   - recursive depth: 3
   - skipped prefix: `#`
   - max file size: 15 MB
5. One `BlockInserter` instance is reused for the batch so placement state is preserved.

## 2. Single DWG Processing

`MergeCoordinator.MergeSingleFile` handles one file:

1. `FileHelper.TryValidateFile` checks existence, read access, and non-zero size.
2. `FileHelper.TryValidateDwgStructure` opens the DWG in a side database and closes input with `CloseInput(true)`.
3. `ViewportLayoutExporter.ExportToTempAsync` exports the first Paper Space layout into a temporary DWG.
4. Temporary DWG bounds are read from a side database.
5. `BlockInserter.InsertNativeObjects` clones all temporary Model Space entities into the target drawing.
6. The temporary file is deleted in `finally`.

Failures at this level return `MergeResult.Warn` or `MergeResult.Fail`; the batch continues with the next file.

## 3. Layout Export

`ViewportLayoutExporter` opens the source DWG in a side `Database`, calls `CloseInput(true)`, finds the first non-model layout, then chooses one of three paths:

| Case | Path | Behavior |
| :--- | :--- | :--- |
| 0 viewports | `ProcessNoVp` | Moves Paper Space entities to Model Space and scales by `MaxScaleMultiplier` (`100`). |
| 1 viewport | `ProcessSingleVp` | Clamps the viewport scale if needed, scales Model Space, then maps Paper Space through the viewport. |
| 2+ viewports | `ProcessMultiVp` | Picks the main viewport, clones auxiliary viewport model content into the main viewport coordinate system, scales Model Space once, then maps Paper Space. |

After Paper Space is cloned into Model Space, `ModelSpaceTrimmer.TrimOutside` removes entities whose extents do not intersect the cloned frame bounds.

## 4. Viewport Math

Main formulas live in `ViewportTransformer`:

- `BuildPaperToMainMatrix`: Paper Space to main viewport model coordinates.
- `BuildMatrix`: auxiliary viewport model coordinates to main viewport model coordinates.
- `ScaleModelSpaceObjects`: applies clamp scaling while compensating `Dimension.Dimscale`, `Dimension.Dimlfac`, and `MLeader.Scale`.

Associative hatches are skipped during direct scaling because their boundary entities update them. Non-associative hatches are transformed and then evaluated.

## 5. Raster Handling

Eligible `RasterImage` entities originally in Model Space are embedded into the temporary DWG as `OLE2FRAME` objects:

1. The code snapshots Model Space object IDs.
2. It copies the image to Clipboard and calls `._PASTECLIP`.
3. The new `OLE2FRAME` is found by comparing the post-insert object set with the snapshot.
4. Size is applied through `WcsWidth` / `WcsHeight`; if that is unreliable, `Position3d` is used.
5. The source `RasterImage` is erased only after a new OLE object is found.
6. The previous Clipboard content is restored in `finally`.

Images larger than 5 MB are not converted to OLE.

## 6. Target Insertion

`BlockInserter.InsertNativeObjects`:

1. Opens the temporary DWG in a side database and closes input with `CloseInput(true)`.
2. Collects Model Space object IDs.
3. Uses `WblockCloneObjects(..., DuplicateRecordCloning.Ignore, ...)` to clone native entities into target Model Space.
4. Applies a displacement to every cloned entity.
5. Computes resulting world bounds from cloned extents; if extents are unavailable, transforms the source bounds.
6. Updates `_rightMax` for the next sheet placement.

The output is not wrapped in `BlockReference`; objects remain directly editable.

## 7. Finalization

After all files:

1. `RasterImagePathFixer.CopyImagesToTargetFolder` copies unresolved raster files next to the result DWG and reuses one copy for duplicate source paths.
2. `DwgOptimizer.Optimize` purges unused symbols in bounded passes.
3. `SaveMerged` writes `DwgVersion.AC1032`.
4. AutoCAD runs `REGENALL` and `ZOOM EXTENTS`.

## Contentious Points

- Should source layer/style conflicts use `DuplicateRecordCloning.MangleName` instead of `Ignore` to preserve visual fidelity?
- Should raster embedding be replaced with direct OLE API creation or should large/problematic images remain as external `RasterImage` references?
- Should processing all layouts be added as a separate command mode?
