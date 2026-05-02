# Detailed Optimization Plan for AutoBIMFusion

This document outlines the specific technical steps required to implement the optimizations described in `TECHNICAL_DOCUMENTATION.md` and `ALGORITHM.md`.

## 1. Eliminating Redundant Disk I/O

Currently, `MergeCoordinator` orchestrates the flow by writing a temporary DWG and then reading it back twice.

### 1.1. Refactor `ViewportLayoutExporter`
- **File:** `AutoBIMFusion/Application/Merge/Layouts/ViewportLayoutExporter.cs`
- **Change:**
    - Change return type of `ExportToTempAsync` from `Task<string>` to `Database`.
    - Rename to `PrepareDatabaseForMerge`.
    - Remove `db.SaveAs(tempPath, DwgVersion.AC1032)`.
    - Ensure the `Database` object is NOT disposed of inside the method (remove the `using` block around the database creation, or return it from the block).

### 1.2. Refactor `MergeCoordinator`
- **File:** `AutoBIMFusion/Application/Merge/MergeOrchestrator.cs`
- **Change:**
    - Call the new `PrepareDatabaseForMerge` and hold the `Database` in a `using` block.
    - Update `ReadBounds` to accept `Database` instead of `string tempPath`.
    - Update `inserter.InsertNativeObjects` signature to accept `Database sourceDb` instead of `string sourceFilePath`.

### 1.3. Refactor `BlockInserter`
- **File:** `AutoBIMFusion/Application/Merge/BlockInserter.cs`
- **Change:**
    - Remove the `using Database sourceDb = new(false, true)` and `sourceDb.ReadDwgFile` logic.
    - Use the provided `sourceDb` directly.

## 2. Consolidating Transformations & Cleanup

### 2.1. Single-Pass Transformation
- **File:** `AutoBIMFusion/Application/Merge/BlockInserter.cs`
- **Change:**
    - Inside the `WblockCloneObjects` post-processing loop (where `TransformBy` is called), immediately perform dimension cleanup instead of collecting IDs and calling `DimensionHealer` later.

### 2.2. Move Unit Normalization
- **File:** `AutoBIMFusion/Application/Merge/BlockInserter.cs`
- **Change:**
    - Ensure `sourceDb.Insunits` and `sourceDb.Measurement` are set once before `WblockCloneObjects` starts, and remove redundant unit syncs elsewhere.

## 3. Code Unification

### 3.1. Unified Dimension Utility
- **Task:** Create `AutoBIMFusion/Application/Merge/Layouts/DimensionUtils.cs`.
- **Content:** Move `RemoveDimStyleOverrides` from `EntityTransformUtils.cs` and related logic from `DimensionHealer.cs` here.
- **Cleanup:** Update all callers to use the new utility.

### 3.2. Simplify Layout Projection
- **File:** `AutoBIMFusion/Application/Merge/Layouts/LayoutProjectionProcessor.cs`
- **Change:**
    - Refactor `ProjectSingleViewport` to call `ProjectMultipleViewports` with a collection containing only one viewport, removing the duplicate math for single-VP scenarios.

## 4. Verification & Testing
- **Check 1:** Run `MERGEDWG_DIAG_TEST` and verify that dimension scaling remains correct.
- **Check 2:** Verify that no temporary `.dwg` files are left in `%TEMP%` after a merge run.
- **Check 3:** Profile the merge of 10+ large files to measure the speedup from reduced I/O.
