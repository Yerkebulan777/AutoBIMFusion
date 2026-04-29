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
        DimensionStyleDiagnosticUtils.cs
        ModelSpaceTrimmer.cs
        DrawOrderPreserver.cs
        ExtentsUtils.cs
        LayoutViewportInfo.cs
        Transforms/
          EntityTransformUtils.cs
          DimensionTransformUtils.cs
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

### MERGEDWG_DIAG_TEST

`MERGEDWG_DIAG_TEST` is a diagnostic-only entry point for repeatable dimension scaling debugging. It bypasses `FolderSelector` and always processes `C:\Users\y.zhumabayev\Desktop\TEST`.

Behavior:

- uses the same merge pipeline as `MERGEDWG`;
- suppresses the final summary `MessageBox`;
- saves to `C:\Users\y.zhumabayev\Desktop\TEST.dwg`;
- logs the source folder, save path, and active Serilog file path;
- preserves `[DIM-DIAG]` debug output from `DimensionTransformUtils`;
- logs compact `[DIM-STYLE]` snapshots from the target `DimStyleTable` before merge, after each file, and before save.

Recommended AutoCAD script flow:

```text
FILEDIA
0
CMDDIA
0
(command "_.NETLOAD" "C:/path/to/AutoBIMFusion.dll")
MERGEDWG_DIAG_TEST
```

When running iterative diagnostics, close AutoCAD before rebuilding if the loaded plugin DLL is locked. The log file is expected under the loaded bundle or DLL folder: `Contents\Logs\merge-YYYY-MM-DD.log` or `Logs\merge-YYYY-MM-DD.log`.

For background runs, prefer `tools\Run-MergeDwgDiagTest.ps1`. It builds a `CoreConsoleDiagnostics` variant without Ribbon/UI startup code, loads the DLL from `AutoBIMFusion\bin\<Configuration>-core\AutoBIMFusion.bundle\Contents`, generates the script, sets `FILEDIA=0`, `CMDDIA=0`, `SECURELOAD=0` for that diagnostic process, discards the temporary drawing on `QUIT` with `_Y`, runs `accoreconsole.exe` with a timeout, and writes Core Console stdout/stderr under `AutoBIMFusion\bin\<Configuration>-core\diag`.

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

`AILog` writes non-debug messages to the AutoCAD editor and all levels to Serilog.

Logging policy:

- command start/end: `Info`
- expected recoverable skips: `Warn`
- AutoCAD API failures that affect a command or file: `Error`
- high-volume diagnostics: `Debug`

Dimension scaling diagnostics use `[DIM-DIAG]` debug entries. Each entry records `scenario`, `stage`, `handle`, entity type, `scaleFactor`, scale order, `measurement`, `measurementRatio`, override text, entity dimension values, linked `DimStyleTableRecord` values, `entityDiffersFromStyle`, visual text height, visual arrow size, dimension geometry points, bounding-box height, and extents.

`MERGEDWG_DIAG_TEST` also writes `[DIM-STYLE]` snapshots for the target dimension style table. These lines are compact, machine-readable equivalents of the manual `ExportDimStylesToMd` report and allow the diagnostic log to distinguish entity overrides from style-table conflicts caused by style cloning.

The root cause found during `MERGEDWG_DIAG_TEST` was double visual scaling: `TransformBy()` already scales dimension geometry, while the previous `Dimscale *= scaleFactor` made recomputed text/arrows grow again. The current compensation keeps `Dimscale` visually neutral and uses `Dimlfac /= scaleFactor` only for numeric value compensation. `MERGEDWG_DIAG_TEST` emits `[DIM-DIAG-SUMMARY]` lines grouped by `scenario`, `scaleFactor`, and `stage`; use `visualSizePreserved` to judge text/arrow stability and `bboxScaledLikeTransform` to identify full-extents growth from extension-line geometry.

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
