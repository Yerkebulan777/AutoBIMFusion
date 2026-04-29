# Graph Report - AutoBIMFusion  (2026-04-29)

## Corpus Check
- 49 files · ~19,575 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 269 nodes · 512 edges · 19 communities detected
- Extraction: 66% EXTRACTED · 34% INFERRED · 0% AMBIGUOUS · INFERRED: 176 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- [[_COMMUNITY_Community 0|Community 0]]
- [[_COMMUNITY_Community 1|Community 1]]
- [[_COMMUNITY_Community 2|Community 2]]
- [[_COMMUNITY_Community 3|Community 3]]
- [[_COMMUNITY_Community 4|Community 4]]
- [[_COMMUNITY_Community 5|Community 5]]
- [[_COMMUNITY_Community 6|Community 6]]
- [[_COMMUNITY_Community 7|Community 7]]
- [[_COMMUNITY_Community 8|Community 8]]
- [[_COMMUNITY_Community 9|Community 9]]
- [[_COMMUNITY_Community 10|Community 10]]
- [[_COMMUNITY_Community 11|Community 11]]
- [[_COMMUNITY_Community 12|Community 12]]
- [[_COMMUNITY_Community 13|Community 13]]
- [[_COMMUNITY_Community 14|Community 14]]
- [[_COMMUNITY_Community 15|Community 15]]
- [[_COMMUNITY_Community 16|Community 16]]
- [[_COMMUNITY_Community 17|Community 17]]
- [[_COMMUNITY_Community 18|Community 18]]

## God Nodes (most connected - your core abstractions)
1. `DimensionTransformUtils` - 22 edges
2. `SmartTextCommands` - 12 edges
3. `DimensionStyleDiagnosticUtils` - 12 edges
4. `TransmittalCommands` - 11 edges
5. `ExtentsUtils` - 11 edges
6. `TextStyleCommands` - 10 edges
7. `ViewportTransformer` - 10 edges
8. `MergeCommands` - 9 edges
9. `LayoutProjectionProcessor` - 9 edges
10. `AILog` - 7 edges

## Surprising Connections (you probably didn't know these)
- None detected - all connections are within the same source files.

## Communities

### Community 0 - "Community 0"
Cohesion: 0.11
Nodes (6): DrawOrderPreserver, ExtentsUtils, LayoutProjectionProcessor, ModelSpaceTrimmer, ViewportTransformer, PickMainViewport()

### Community 1 - "Community 1"
Cohesion: 0.12
Nodes (6): JoinCommands, LineInfo, MergeCommands, AILog, FileEnumerator, UiDialogService

### Community 2 - "Community 2"
Cohesion: 0.18
Nodes (2): DiagnosticSummaryStats, DimensionTransformUtils

### Community 3 - "Community 3"
Cohesion: 0.11
Nodes (7): MergeCoordinator, Fail(), Ok(), Warn(), MergeStatistics, StringUtils, FileHelper

### Community 4 - "Community 4"
Cohesion: 0.16
Nodes (3): TransmittalCommands, RasterImagePathFixer, RibbonIconLoader

### Community 5 - "Community 5"
Cohesion: 0.21
Nodes (3): ViewportCollector, ViewportLayoutExporter, LayoutUtil

### Community 6 - "Community 6"
Cohesion: 0.28
Nodes (1): SmartTextCommands

### Community 7 - "Community 7"
Cohesion: 0.27
Nodes (1): DimensionStyleDiagnosticUtils

### Community 8 - "Community 8"
Cohesion: 0.33
Nodes (1): TextStyleCommands

### Community 9 - "Community 9"
Cohesion: 0.22
Nodes (3): AutoBIMFusionExtension, IExtensionApplication, RibbonBuilder

### Community 10 - "Community 10"
Cohesion: 0.31
Nodes (2): StyleExportCommands, StyleExportUtils

### Community 11 - "Community 11"
Cohesion: 0.31
Nodes (3): ILogEventSink, DiagnosticSink, LoggerFactory

### Community 12 - "Community 12"
Cohesion: 0.39
Nodes (4): AcadWarningSuppressScope, LayoutEditScope, ManagedSystemVariable, IDisposable

### Community 13 - "Community 13"
Cohesion: 0.53
Nodes (1): DwgOptimizer

### Community 14 - "Community 14"
Cohesion: 0.4
Nodes (1): BlockInserter

### Community 15 - "Community 15"
Cohesion: 0.47
Nodes (1): EntityTransformUtils

### Community 16 - "Community 16"
Cohesion: 0.4
Nodes (2): ICommand, ButtonCommandHandler

### Community 17 - "Community 17"
Cohesion: 0.5
Nodes (2): IComparer, WindowsNaturalComparer

### Community 18 - "Community 18"
Cohesion: 0.67
Nodes (1): FolderSelector

## Knowledge Gaps
- **2 isolated node(s):** `LineInfo`, `DiagnosticSummaryStats`
  These have ≤1 connection - possible missing edges or undocumented components.
- **Thin community `Community 2`** (23 nodes): `DimensionTransformUtils.cs`, `.ToString()`, `DiagnosticSummaryStats`, `DimensionTransformUtils`, `.AdjustDimensionScale()`, `.CaptureSnapshot()`, `.EscapeDiagnosticText()`, `.FormatColor()`, `.FormatDimensionGeometry()`, `.FormatDouble()`, `.FormatNullableDouble()`, `.FormatRatio()`, `.HasChanged()`, `.IsMeasurementApplicable()`, `.IsRatioNear()`, `.IsSame()`, `.LogDimensionDiagnostic()`, `.ReadDouble()`, `.ReadNullableDouble()`, `.ReadText()`, `.RecordSummary()`, `.RoundScaleFactor()`, `.TransformDimension()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 6`** (13 nodes): `SmartTextCommands.cs`, `SmartTextCommands`, `.AreHeightsClose()`, `.AreTextsClose()`, `.CollectTextElements()`, `.CombineGroupText()`, `.EscapeMTextContent()`, `.EstimateTextWidth()`, `.GetTextBoundsAlongAxis()`, `.LowerBoundByPerp()`, `.ProjectPerpendicular()`, `.SmartGroupText()`, `.SmartMergeModelText()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 7`** (13 nodes): `DimensionStyleDiagnosticUtils.cs`, `DimensionStyleDiagnosticUtils`, `.CreateSnapshot()`, `.EntityDiffersFromStyle()`, `.Escape()`, `.FormatColor()`, `.FormatDouble()`, `.FormatSnapshot()`, `.FormatStyle()`, `.FormatStyleSnapshot()`, `.LogDimensionStyleSnapshot()`, `.Near()`, `.TryReadStyleSnapshot()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 8`** (11 nodes): `TextStyleCommands.cs`, `TextStyleCommands`, `.BuildSignature()`, `.ChooseMasterStyle()`, `.CollectStyles()`, `.DeleteStyles()`, `.MergeTextStyles()`, `.Normalize()`, `.ReassignBlockAttributes()`, `.ReassignStyles()`, `.ReassignStylesInBlock()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 10`** (9 nodes): `StyleExportCommands.cs`, `StyleExportUtils.cs`, `StyleExportCommands`, `.ExportDimStyles()`, `.ExportTextStyles()`, `StyleExportUtils`, `.DumpProperties()`, `.ExportSymbolTableToMd()`, `.GetPropertyDisplayName()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 13`** (6 nodes): `DwgOptimizer.cs`, `DwgOptimizer`, `.AddDictionaryIds()`, `.AddTableIds()`, `.Optimize()`, `.PurgePass()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 14`** (6 nodes): `BlockInserter.cs`, `.Transform()`, `.Union()`, `BlockInserter`, `.CalcInsertionPoint()`, `.InsertNativeObjects()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 15`** (6 nodes): `EntityTransformUtils.cs`, `EntityTransformUtils`, `.AdjustMLeaderScale()`, `.EvaluateHatch()`, `.GetScaleFactor()`, `.TransformEntity()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 16`** (5 nodes): `ButtonCommandHandler.cs`, `ICommand`, `ButtonCommandHandler`, `.CanExecute()`, `.Execute()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 17`** (5 nodes): `WindowsNaturalComparer.cs`, `IComparer`, `WindowsNaturalComparer`, `.Compare()`, `.StrCmpLogicalW()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 18`** (3 nodes): `FolderSelector.cs`, `FolderSelector`, `.TrySelectFolder()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `DimensionTransformUtils` connect `Community 2` to `Community 1`?**
  _High betweenness centrality (0.057) - this node is a cross-community bridge._
- **What connects `LineInfo`, `DiagnosticSummaryStats` to the rest of the system?**
  _2 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.11 - nodes in this community are weakly interconnected._
- **Should `Community 1` be split into smaller, more focused modules?**
  _Cohesion score 0.12 - nodes in this community are weakly interconnected._
- **Should `Community 3` be split into smaller, more focused modules?**
  _Cohesion score 0.11 - nodes in this community are weakly interconnected._