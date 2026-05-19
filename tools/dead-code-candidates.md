# Dead Code Candidates — AutoBIMFusion

**Entry points:** `MERGEDWG` (`MergeDwgFolderCommand`) + `MERGEDWG_BATCH` (`MergeDwgBatchCommand`)
**Method:** GitNexus graph (Pass 1) + Roslynator (Pass 2, run `tools/Find-DeadCode.ps1`)
**Last Pass 1 scan:** 2026-05-19

---

## Pass 1 — GitNexus: Methods With No Incoming CALLS

> These methods have zero callers in the indexed call graph.
> ⚠ Extension methods may be false positives (GitNexus doesn't always capture extension call syntax).
> ⚠ Event handlers registered via `+=` won't have CALLS edges — check before removing.

### 🔴 High Confidence — Merge/Plugin Projects

These are in core business logic (not utility library). Confirmed by grep: no callers found.

| Symbol | File | Line | Notes |
|--------|------|------|-------|
| `AppendDimStyleProperties` | [DimensionStyleDiagnosticUtils.cs](../src/AutoBIMFusion.Merge/Combine/Layouts/DimensionStyleDiagnosticUtils.cs) | 239 | `private static` — duplicate of `FormatUtils.AppendDimStyleProperties` |
| `AppendProperties` | [DimensionStyleDiagnosticUtils.cs](../src/AutoBIMFusion.Merge/Combine/Layouts/DimensionStyleDiagnosticUtils.cs) | 286 | `private static` — duplicate of `FormatUtils.AppendProperties` |
| `GetOrCreateStandardDimensionStyle` | [StyleUnificationService.cs](../src/AutoBIMFusion.Merge/Combine/Layouts/StyleUnificationService.cs) | 79 | `internal static` — grep confirms zero callers |

> `ScaleList` (DrawingPurger) appeared in GitNexus index but doesn't exist in source — stale index entry, ignore.

---

### 🟡 Medium Confidence — Common Extensions (273 methods, no CALLS edges)

Extension methods are invoked via dot-syntax (`obj.Method()`) — GitNexus may miss these as CALLS edges.
**Do NOT remove without Roslynator confirmation.**

Run `tools/Find-DeadCode.ps1` to get Roslynator analysis. Only remove entries flagged by BOTH methods.

Representative samples from GitNexus scan (not exhaustive — see full Cypher results):

#### AutoBIMFusion.Common/Drawing/BlockReferences.cs
- `CreateFromExistingEnts` (L146), `ReplaceAllBlockReference` (L47), `Create` (L102)
- `RenameBlockAndInsert` (L234), `GetUniqueBlockName` (L309), `IsBlockExist` (L326)
- `InitForTransient` (L543)

#### AutoBIMFusion.Common/Extensions/
High-volume files with potentially unused extension methods:
- **Arcs.cs**: `ToCircularArc2d`, `GetArcBulge`
- **Bitmap.cs**: `GetImageFileSize`, `RotateImage`
- **BlockReference.cs**: `ProjectXrefPointToCurrentSpace`, `SetAttributeValues`, `GetAttributesValues`, `SetDynamicBlockReferenceProperty`, `GetBlocDefinition`, `GetDynamicBlockHandleFromAnonymousBlock`, `IsLayoutOrModel`, `IsXref`, `RegenAllBlkDefinition`, `IsThereABlockReference`
- **Colors.cs**: `FromHSV`, `ColorToHSV`, `SetContrast`, `SetBrightness`, `ColorToHex`, `GetRealColor`, `ConvertColorToGray`, `GetTransGraphicsColor`, `GetSystemDrawingColor`
- **Curves.cs**: `ConvertToCurve`, `Reverse`, `OffsetPolyline`, `ToOrderedArray`, `GetParamAtPointX`, `RegionMerge`, `JoinMerge`
- **Database.cs**: `GetSize`, `GetDwgVersion`, `StoreObjectIdInAppDictionary`, `GetObjectIdFromAppDictionary`, `SetAnnotativeScale`, `EntLast`, `GetAllEntities`, `OpenAsNewTab`, `GetAllObjects`
- **Editor.cs**: `GetSelectionRedraw`, `GetPolyline`, `AddToImpliedSelection`, `GetCurves`, `GetOptions`, `GetBlock`, `GetBlocks`, `GetViewport`, `GetLayoutFromName`, `GetModelLayout`, `GetCurrentViewBound`, `GetUSCRotation`, `ViewPlan`, `IsInPaperSpace`, `IsInLockedViewport`, `GetHatch`
- **Entity.cs**: `TransformToFitBoundingBox`, `AddXData`, `ReadXData`, `RemoveXData`, `TryGetArea`, `CopyDrawOrderTo`, `EraseObject`
- **Extends3d.cs**: `GetGeometry`, `GetCenter`, `IsInside`, `Expand`, `GetExplodedExtents`, `GetVisualExtents`, multiple `GetExtents` overloads, `IsFullyInside`, `CollideWithOrConnected`, `Middle`, `CollideWith`, `Size`, `ZoomExtents`, `ToRectangle3d`
- **Hatchs.cs**: `RemoveAllLoops`, `GetPolyHole`, `GetAssociatedBoundary`, `HatchRegion`
- **Lines.cs**: `IsLineSegIntersect`, `GetVector3d`, `IsLinePassesThroughPoint`, `IsCutting` (x2), `ToPolyline` (x2)
- **List.cs**: `RemoveCommun`, `HasTypeOf`, `SumNumeric`, `AddRangeUnique`, `DeepDispose`
- **ObjectId.cs**: `ToList` (x2), `ToObjectIdCollection`, `ToDBObjectCollection` (x2), `GetNoTransactionDBObject`, `GetObjectByteSize`, `IsValidForOperation`, `HatchObject`, `Join`, `EraseObject`
- **Point3d.cs**: `GetMiddlePoint`, `ToPoints`, `Flatten`, `AddToDrawing`, `IsPointInsidePolygon`, `GetArea`, `OrderByDistanceFromPoint`, `OrderByDistanceOnLine`, `GetDisplacementMatrixTo`, `IsInsidePolyline`, `IsOnPolyline`, `TranformToBlockReferenceTransformation`, `ContainsAll`, `GetAngleWith`, `GetIntermediatePoint`
- **Polylines.cs**: `IsOverlaping`, `IsSameAs`, `IsInside`, `IsAtLeftSide`, `IsAtRightSide`, `FixNormals`, `Flatten`, `HasAngle`, `SmartOffset`, `GetSpline`, `GetCentroid`, `CleanupPolylines`, `BreakAt`, `GetPassingThroughBulgeFrom`, `ContainsSegment`, `IsSegmentIntersecting`, `JoinPolyline`, `AddVertexIfNotExist`, `AddVertex` (x2), `GetBulgeBetween`, `GetPolylineFromPoints`
- **String.cs**: `RemoveNonNumeric`, `AllIndexesOf`, `UcFirst`, `TrimStart`, `RemoveDiacritics`, `IgnoreCaseEquals`, `SplitUserInputByDelimiters`, `SanitizeToAlphanumericHyphens`
- **Vector3d.cs**: `IsColinear`, `ToVector3d`, `GetRotationRelativeToSCG`, `Inverse` (x2), `IsVectorOnRightSide`, `SetLength`, `DrawVector`, `FindProjectedIntersection`
- **Viewports.cs**: `ComputeModelWindow`, `ResolveCustomScale`, `GetViewCenterWcs`, `GetBoundary`, `GetAllViewportsInPaperSpace`, `ModelToPaper` (x2), `PaperToModel` (x2), `GetViewPortsNumbers`, `IsInLayoutViewport`

#### AutoBIMFusion.Common/Helpers/
- **FileUtil.cs**: `GetLinkerTime`
- **LayoutUtil.cs**: `GetPaperSpaceEntities`, `GetLayoutBtrId`
- **NumericUtils.cs**: `Clamp`, `IntermediatePercentage`, `IsBetween`, `RoundToNearestMultiple`
- **ReflectionHelper.cs**: `SafeGetTypes`

#### AutoBIMFusion.Common/Mist/
- **HightLighter.cs**: `RegisterHighlight`, `RegisterUnhighlight`, `UnhighlightAll`
- **Layers.cs**: `IsLayerLocked`, `SetLayerColor`, `Rename`, `Merge` (x2), `CreateLayer`, `GetTransparency`, `SetTransparency`, `GetAllLayersInDrawing`, `IsEntityOnLockedLayer`, `GetLayerColor`
- **SelectInXref.cs**: `GetEntityPathInChildXref`
- **Generic.cs**: `GetLock`, `GetTrans`, `GetTransparencyFromAlpha`, `AddFontStyle`, `GetExtensionDLLLocation`, `LoadLispFromStringCommand`, `WriteInfoCenterBalloonMessage`, `TryFormatIfNumberForPrint`, `GetCurrentDocumentPath`, `GetSaveVersion`, `CommandAsyncInCommandContext`, `CommandInApplicationContext`, `RegenALLCommand`, `RegenCommand`
- **Geometry/Arythmetique.cs**: `ComputeSlopeAndIntermediate`, `ComputePointFromSlopePourcentage`, `FindDistanceToAltitudeBetweenTwoPoint`
- **Geometry/CotePoints.cs**: `NullPointExit`, `GetAltitudeFromBloc`, `GetCotePoints`
- **Geometry/Points.cs**: `Flatten`, `DistanceTo`
- **Geometry/PolygonOperations/**: `GetInnerCentroid`, `Intersection`, `GetBoundaries`, `GetAllHoles`, `Slice`, `Substraction`, `Union`, `CleanPolylineSegments`

#### AutoBIMFusion.Plugin/
- **AutoBIMFusionExtension.cs**: `OnIdle` (L20) — ⚠ **EVENT HANDLER** (`Application.Idle += OnIdle`), **DO NOT DELETE**

---

## Decision Matrix

| Confidence | Both tools flag | Action |
|------------|----------------|--------|
| 🔴 High | Yes | Remove after `dotnet build` verify |
| 🟡 Medium | GitNexus only | Check reflection/events first |
| 🟢 Low | Roslynator only | Verify not used as public API |

---

## Next Steps

1. Run `.\tools\Find-DeadCode.ps1` to get Roslynator pass
2. Cross-reference Roslynator output with this file
3. Start removal with High-confidence items: `.\tools\Remove-DeadCode.ps1 --dry-run`
4. Confirm build passes: `dotnet build src\AutoBIMFusion.Plugin\AutoBIMFusion.Plugin.csproj -c DebugA26`
