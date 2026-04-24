# Graph Report - AutoBIMFusion  (2026-04-25)

## Corpus Check
- 41 files · ~20,067 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 206 nodes · 373 edges · 15 communities detected
- Extraction: 65% EXTRACTED · 35% INFERRED · 0% AMBIGUOUS · INFERRED: 129 edges (avg confidence: 0.8)
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

## God Nodes (most connected - your core abstractions)
1. `ViewportLayoutExporter` - 23 edges
2. `TransmittalCommands` - 11 edges
3. `TextStyleCommands` - 10 edges
4. `SmartTextCommands` - 9 edges
5. `MergeCommands` - 8 edges
6. `ViewportTransformer` - 8 edges
7. `OperationLogger` - 7 edges
8. `MergeStatistics` - 6 edges
9. `GeometryUtils` - 6 edges
10. `AutoBIMFusionExtension` - 5 edges

## Surprising Connections (you probably didn't know these)
- None detected - all connections are within the same source files.

## Communities

### Community 0 - "Community 0"
Cohesion: 0.11
Nodes (6): DrawOrderPreserver, GeometryUtils, LayoutUtil, PickMainViewport(), ModelSpaceTrimmer, ViewportTransformer

### Community 1 - "Community 1"
Cohesion: 0.15
Nodes (4): FileEnumerator, FolderSelector, MergeCommands, MergeStatistics

### Community 2 - "Community 2"
Cohesion: 0.15
Nodes (3): RasterImagePathFixer, RibbonIconLoader, TransmittalCommands

### Community 3 - "Community 3"
Cohesion: 0.23
Nodes (1): ViewportLayoutExporter

### Community 4 - "Community 4"
Cohesion: 0.17
Nodes (6): DwgMerger, FileHelper, Fail(), Ok(), Warn(), StringUtils

### Community 5 - "Community 5"
Cohesion: 0.19
Nodes (4): BlockInserter, JoinCommands, LineInfo, OperationLogger

### Community 6 - "Community 6"
Cohesion: 0.33
Nodes (1): TextStyleCommands

### Community 7 - "Community 7"
Cohesion: 0.36
Nodes (1): SmartTextCommands

### Community 8 - "Community 8"
Cohesion: 0.22
Nodes (3): AutoBIMFusionExtension, IExtensionApplication, RibbonBuilder

### Community 9 - "Community 9"
Cohesion: 0.39
Nodes (4): AcadWarningSuppressScope, LayoutEditScope, ManagedSystemVariable, IDisposable

### Community 10 - "Community 10"
Cohesion: 0.29
Nodes (3): ILogEventSink, DiagnosticSink, LoggerFactory

### Community 11 - "Community 11"
Cohesion: 0.53
Nodes (1): DwgOptimizer

### Community 12 - "Community 12"
Cohesion: 0.6
Nodes (1): ViewportCollector

### Community 13 - "Community 13"
Cohesion: 0.4
Nodes (2): ButtonCommandHandler, ICommand

### Community 14 - "Community 14"
Cohesion: 0.5
Nodes (2): IComparer, WindowsNaturalComparer

## Knowledge Gaps
- **1 isolated node(s):** `LineInfo`
  These have ≤1 connection - possible missing edges or undocumented components.
- **Thin community `Community 3`** (20 nodes): `ViewportLayoutExporter.cs`, `.TryFindFirstLayout()`, `.Warn()`, `ViewportLayoutExporter`, `.AlignOleToTargetMinPoint()`, `.ApplyWcsSize()`, `.BuildTargetRectangle()`, `.BuildTempPath()`, `.CheckIfNeedsOle()`, `.CollectRasterImages()`, `.EmbedSingleRasterAsync()`, `.ExportToTempAsync()`, `.FindNewOle2Frame()`, `.GetModelSpaceSnapshot()`, `.IsCloseToTarget()`, `.ResizeOleToTarget()`, `.ResolveRasterPath()`, `.RunOleEmbeddingAsync()`, `.TryApplyPositionFallback()`, `.TryCopyImageToClipboard()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 6`** (11 nodes): `TextStyleCommands.cs`, `TextStyleCommands`, `.BuildSignature()`, `.ChooseMasterStyle()`, `.CollectStyles()`, `.DeleteStyles()`, `.MergeTextStyles()`, `.Normalize()`, `.ReassignBlockAttributes()`, `.ReassignStyles()`, `.ReassignStylesInBlock()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 7`** (10 nodes): `SmartTextCommands.cs`, `SmartTextCommands`, `.AreTextsClose()`, `.CollectTextElements()`, `.CombineGroupText()`, `.EscapeMTextContent()`, `.EstimateTextWidth()`, `.GetTextBoundsAlongAxis()`, `.SmartGroupText()`, `.SmartMergeModelText()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 11`** (6 nodes): `DwgOptimizer.cs`, `DwgOptimizer`, `.AddDictionaryIds()`, `.AddTableIds()`, `.Optimize()`, `.PurgePass()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 12`** (5 nodes): `ViewportCollector.cs`, `ViewportCollector`, `.Collect()`, `.ComputeModelWindow()`, `.ResolveScale()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 13`** (5 nodes): `ButtonCommandHandler.cs`, `ButtonCommandHandler`, `.CanExecute()`, `.Execute()`, `ICommand`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 14`** (5 nodes): `WindowsNaturalComparer.cs`, `IComparer`, `WindowsNaturalComparer`, `.Compare()`, `.StrCmpLogicalW()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `ViewportLayoutExporter` connect `Community 3` to `Community 0`?**
  _High betweenness centrality (0.039) - this node is a cross-community bridge._
- **What connects `LineInfo` to the rest of the system?**
  _1 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.11 - nodes in this community are weakly interconnected._