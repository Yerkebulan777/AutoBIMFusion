# Graph Report - AutoBIMFusion  (2026-04-26)

## Corpus Check
- 41 files · ~20,604 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 208 nodes · 391 edges · 15 communities detected
- Extraction: 64% EXTRACTED · 36% INFERRED · 0% AMBIGUOUS · INFERRED: 142 edges (avg confidence: 0.8)
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
10. `ViewportCollector` - 6 edges

## Surprising Connections (you probably didn't know these)
- None detected - all connections are within the same source files.

## Communities

### Community 0 - "Community 0"
Cohesion: 0.14
Nodes (5): DrawOrderPreserver, GeometryUtils, PickMainViewport(), ModelSpaceTrimmer, ViewportTransformer

### Community 1 - "Community 1"
Cohesion: 0.16
Nodes (2): LayoutUtil, ViewportLayoutExporter

### Community 2 - "Community 2"
Cohesion: 0.13
Nodes (6): FileEnumerator, FolderSelector, JoinCommands, LineInfo, MergeCommands, OperationLogger

### Community 3 - "Community 3"
Cohesion: 0.13
Nodes (7): BlockInserter, DwgMerger, FileHelper, Fail(), Ok(), Warn(), StringUtils

### Community 4 - "Community 4"
Cohesion: 0.12
Nodes (5): AutoBIMFusionExtension, IExtensionApplication, RasterImagePathFixer, RibbonBuilder, RibbonIconLoader

### Community 5 - "Community 5"
Cohesion: 0.33
Nodes (1): TextStyleCommands

### Community 6 - "Community 6"
Cohesion: 0.31
Nodes (1): TransmittalCommands

### Community 7 - "Community 7"
Cohesion: 0.36
Nodes (1): SmartTextCommands

### Community 8 - "Community 8"
Cohesion: 0.39
Nodes (4): AcadWarningSuppressScope, LayoutEditScope, ManagedSystemVariable, IDisposable

### Community 9 - "Community 9"
Cohesion: 0.43
Nodes (1): MergeStatistics

### Community 10 - "Community 10"
Cohesion: 0.52
Nodes (1): ViewportCollector

### Community 11 - "Community 11"
Cohesion: 0.29
Nodes (3): ILogEventSink, DiagnosticSink, LoggerFactory

### Community 12 - "Community 12"
Cohesion: 0.53
Nodes (1): DwgOptimizer

### Community 13 - "Community 13"
Cohesion: 0.4
Nodes (2): ButtonCommandHandler, ICommand

### Community 14 - "Community 14"
Cohesion: 0.5
Nodes (2): IComparer, WindowsNaturalComparer

## Knowledge Gaps
- **1 isolated node(s):** `LineInfo`
  These have ≤1 connection - possible missing edges or undocumented components.
- **Thin community `Community 1`** (27 nodes): `ViewportLayoutExporter.cs`, `LayoutUtil.cs`, `LayoutUtil`, `.GetLayoutBtrId()`, `.GetPaperSpaceEntities()`, `.TryFindFirstLayout()`, `.Warn()`, `ViewportLayoutExporter`, `.AlignOleToTargetMinPoint()`, `.ApplyWcsSize()`, `.BuildTargetRectangle()`, `.BuildTempPath()`, `.CheckIfNeedsOle()`, `.CollectRasterImages()`, `.EmbedSingleRasterAsync()`, `.EraseBlockContents()`, `.ExportToTempAsync()`, `.FindNewOle2Frame()`, `.GetModelSpaceSnapshot()`, `.IsCloseToTarget()`, `.MovePaperToModelSpace()`, `.ProcessNoVp()`, `.ResizeOleToTarget()`, `.ResolveRasterPath()`, `.RunOleEmbeddingAsync()`, `.TryApplyPositionFallback()`, `.TryCopyImageToClipboard()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 5`** (11 nodes): `TextStyleCommands.cs`, `TextStyleCommands`, `.BuildSignature()`, `.ChooseMasterStyle()`, `.CollectStyles()`, `.DeleteStyles()`, `.MergeTextStyles()`, `.Normalize()`, `.ReassignBlockAttributes()`, `.ReassignStyles()`, `.ReassignStylesInBlock()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 6`** (11 nodes): `TransmittalCommands.cs`, `TransmittalCommands`, `.ConfigureTransmittalInfo()`, `.ConvertMemberValue()`, `.CreateETransmitZip()`, `.PrepareOutputFolders()`, `.SafeGetTypes()`, `.SetMemberValue()`, `.TryCreateTransmittalOperation()`, `.TryDeleteTempFolder()`, `.TryLoadAssemblyByPath()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 7`** (10 nodes): `SmartTextCommands.cs`, `SmartTextCommands`, `.AreTextsClose()`, `.CollectTextElements()`, `.CombineGroupText()`, `.EscapeMTextContent()`, `.EstimateTextWidth()`, `.GetTextBoundsAlongAxis()`, `.SmartGroupText()`, `.SmartMergeModelText()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 9`** (7 nodes): `MergeStatistics.cs`, `.MergeFiles()`, `MergeStatistics`, `.RecordFailed()`, `.RecordSkipped()`, `.RecordSuccess()`, `.RecordTotal()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 10`** (7 nodes): `ViewportCollector.cs`, `ViewportCollector`, `.Collect()`, `.ComputeModelWindow()`, `.GetDcsToWcsMatrix()`, `.GetViewCenterWcs()`, `.ResolveScale()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 12`** (6 nodes): `DwgOptimizer.cs`, `DwgOptimizer`, `.AddDictionaryIds()`, `.AddTableIds()`, `.Optimize()`, `.PurgePass()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 13`** (5 nodes): `ButtonCommandHandler.cs`, `ButtonCommandHandler`, `.CanExecute()`, `.Execute()`, `ICommand`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 14`** (5 nodes): `WindowsNaturalComparer.cs`, `IComparer`, `WindowsNaturalComparer`, `.Compare()`, `.StrCmpLogicalW()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `ViewportLayoutExporter` connect `Community 1` to `Community 0`?**
  _High betweenness centrality (0.042) - this node is a cross-community bridge._
- **Why does `TransmittalCommands` connect `Community 6` to `Community 4`?**
  _High betweenness centrality (0.037) - this node is a cross-community bridge._
- **What connects `LineInfo` to the rest of the system?**
  _1 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.14 - nodes in this community are weakly interconnected._
- **Should `Community 2` be split into smaller, more focused modules?**
  _Cohesion score 0.13 - nodes in this community are weakly interconnected._
- **Should `Community 3` be split into smaller, more focused modules?**
  _Cohesion score 0.13 - nodes in this community are weakly interconnected._
- **Should `Community 4` be split into smaller, more focused modules?**
  _Cohesion score 0.12 - nodes in this community are weakly interconnected._