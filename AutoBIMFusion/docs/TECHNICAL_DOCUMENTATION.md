# AutoBIMFusion Technical Documentation

**Updated:** 2026-04-29

## 1. Overview

AutoBIMFusion is an AutoCAD plugin targeting .NET 8 and x64 AutoCAD 2025-2027. It provides merge, text cleanup, style cleanup, style export, and eTransmit packaging commands.

The main requirement is stable DWG merging with native, editable entities in the target drawing.

## 2. Project Structure

```text
AutoBIMFusion/
  Application/
    AcadSupport/
      AcadWarningSuppressScope.cs
    Commands/
      MergeCommands.cs
      SmartTextCommands.cs
      TextStyleCommands.cs
      StyleExportCommands.cs
      TransmittalCommands.cs
      JoinCommands.cs
    Merge/
      MergeOrchestrator.cs        # MergeCoordinator class: one-file workflow
      BlockInserter.cs            # Native object insertion and placement
      DwgOptimizer.cs             # Bounded purge passes
      RasterImagePathFixer.cs     # Raster path normalization for final DWG
      Layouts/
        ViewportLayoutExporter.cs
        LayoutProjectionProcessor.cs
        ViewportTransformer.cs
        ViewportCollector.cs
        ModelSpaceTrimmer.cs
        DrawOrderPreserver.cs
        ExtentsUtils.cs
        EntityTransformUtils.cs
        LayoutViewportInfo.cs
      Models/
        MergeResult.cs
        MergeStatistics.cs
    Ribbon/
      RibbonBuilder.cs
      ButtonCommandHandler.cs
      RibbonIconLoader.cs
    Utils/
      FileEnumerator.cs
      FileHelper.cs
      FolderSelector.cs
      LayoutUtil.cs
      StringUtils.cs
      StyleExportUtils.cs
      WindowsNaturalComparer.cs
  Infrastructure/
    Logging/
      OperationLogger.cs
      LoggerFactory.cs
```

## 3. Runtime Flow

### MERGEDWG

1. `MergeCommands` starts the command, creates `OperationLogger`, and rejects parallel runs.
2. `FolderSelector` gets the source folder.
3. `FileEnumerator` collects and naturally sorts DWG paths.
4. `MergeCoordinator.MergeSingleFile` processes each file independently.
5. `ViewportLayoutExporter` exports the first layout to a temporary DWG and delegates projection logic to `LayoutProjectionProcessor`.
6. `LayoutProjectionProcessor` handles 0 / 1 / multi-viewport strategies, including scale clamp, aux-viewport flattening, and Paper Space transfer.
7. `ModelSpaceTrimmer.TrimOutside` removes Model Space entities outside projected frame bounds.
8. `BlockInserter` clones temporary Model Space objects into the target database.
9. Final pass fixes raster paths, purges unused database objects, saves, and regenerates the drawing.

### SMART_MERGE_TEXT

1. Collects non-empty `TEXT` and `MTEXT` from Model Space.
2. Groups candidates by text style and rounded rotation.
3. Sorts each group by the perpendicular text axis.
4. Uses binary search to inspect only nearby vertical candidates instead of scanning the whole group for every text.
5. Creates one `MText` per group and erases source entities.

### MergeTextStyles

1. Builds a signature from each non-dependent text style.
2. Groups duplicate signatures.
3. Chooses the current style as master when possible; otherwise uses the alphabetically first style.
4. Reassigns `DBText`, `MText`, `AttributeDefinition`, and `AttributeReference`.
5. Erases duplicate style records when AutoCAD allows it.

### CreateETransmitZip

1. Requires the active drawing to be saved.
2. Locates AutoCAD eTransmit API types by assembly/type discovery.
3. Configures known eTransmit options through reflection because member names vary by AutoCAD version.
4. Creates a temporary package folder, zips it, and deletes the temporary folder.

## 4. Resource and Transaction Rules

- Every `Transaction`, side `Database`, `DocumentLock`, `ProgressMeter`, image, and warning scope is wrapped in `using`.
- Side databases call `ReadDwgFile` followed by `CloseInput(true)` where the file must not remain locked.
- Temporary DWG files and temporary eTransmit folders are deleted in `finally`.
- Long-running per-file failures return `MergeResult` instead of aborting the whole batch.
- Entity-level transform failures are logged with type and handle, then processing continues.

## 5. Error Handling and Logging

`OperationLogger` writes non-debug messages to the AutoCAD editor and all levels to Serilog.

Logging policy:

- command start/end: `Info`
- expected recoverable skips: `Warn`
- AutoCAD API failures that affect a command or file: `Error`
- high-volume diagnostics: `Debug`

The main command catches startup failures outside the async task body so users see a clear editor message instead of an unobserved task exception.

## 6. Important Constants

| Constant | Location | Value | Meaning |
| :--- | :--- | :--- | :--- |
| `MaxFileSizeBytes` | `FileEnumerator` | 15 MB | Skip large source DWGs. |
| `MaxRecursionDepth` | `FileEnumerator` | 3 | Limit folder traversal. |
| `MaxScaleMultiplier` | `LayoutProjectionProcessor` | 100 | Clamp viewport scale and no-viewport default scale, equivalent to 1:100. |
| `MaxPurgePasses` | `DwgOptimizer` | 5 | Bound purge iterations. |

## 7. Remaining Risks

- `DuplicateRecordCloning.Ignore` is stable for the target database but can change visual style fidelity if source and target definitions conflict.
- Long operations have no cancellation token.
- Only the first Paper Space layout is processed.

See [KNOWN_ISSUES.md](KNOWN_ISSUES.md) for current open items.
