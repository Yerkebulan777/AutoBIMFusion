# Graph Report - AutoBIMFusion  (2026-04-28)

## Corpus Check
- 43 files · ~18,864 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 228 nodes · 437 edges · 14 communities detected
- Extraction: 63% EXTRACTED · 37% INFERRED · 0% AMBIGUOUS · INFERRED: 161 edges (avg confidence: 0.8)
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

## God Nodes (most connected - your core abstractions)
1. `ViewportLayoutExporter` - 26 edges
2. `SmartTextCommands` - 12 edges
3. `TransmittalCommands` - 11 edges
4. `TextStyleCommands` - 10 edges
5. `ExtentsUtils` - 10 edges
6. `ViewportTransformer` - 9 edges
7. `MergeCommands` - 8 edges
8. `OperationLogger` - 7 edges
9. `ViewportCollector` - 6 edges
10. `MergeStatistics` - 6 edges

## Surprising Connections (you probably didn't know these)
- None detected - all connections are within the same source files.

## Communities

### Community 0 - "Community 0"
Cohesion: 0.1
Nodes (7): DrawOrderPreserver, ExtentsUtils, ModelSpaceTrimmer, ViewportTransformer, PickMainViewport(), BlockInserter, LayoutUtil

### Community 1 - "Community 1"
Cohesion: 0.11
Nodes (7): JoinCommands, LineInfo, MergeCommands, DwgOptimizer, FolderSelector, OperationLogger, FileEnumerator

### Community 2 - "Community 2"
Cohesion: 0.11
Nodes (7): MergeCoordinator, Fail(), Ok(), Warn(), MergeStatistics, StringUtils, FileHelper

### Community 3 - "Community 3"
Cohesion: 0.21
Nodes (1): ViewportLayoutExporter

### Community 4 - "Community 4"
Cohesion: 0.13
Nodes (5): AutoBIMFusionExtension, IExtensionApplication, RasterImagePathFixer, RibbonBuilder, RibbonIconLoader

### Community 5 - "Community 5"
Cohesion: 0.28
Nodes (1): SmartTextCommands

### Community 6 - "Community 6"
Cohesion: 0.29
Nodes (1): TransmittalCommands

### Community 7 - "Community 7"
Cohesion: 0.33
Nodes (1): TextStyleCommands

### Community 8 - "Community 8"
Cohesion: 0.31
Nodes (2): StyleExportCommands, StyleExportUtils

### Community 9 - "Community 9"
Cohesion: 0.39
Nodes (4): AcadWarningSuppressScope, LayoutEditScope, ManagedSystemVariable, IDisposable

### Community 10 - "Community 10"
Cohesion: 0.52
Nodes (1): ViewportCollector

### Community 11 - "Community 11"
Cohesion: 0.29
Nodes (3): ILogEventSink, DiagnosticSink, LoggerFactory

### Community 12 - "Community 12"
Cohesion: 0.4
Nodes (2): ICommand, ButtonCommandHandler

### Community 13 - "Community 13"
Cohesion: 0.5
Nodes (2): IComparer, WindowsNaturalComparer

## Knowledge Gaps
- **1 isolated node(s):** `LineInfo`
  These have ≤1 connection - possible missing edges or undocumented components.
- **Thin community `Community 3`** (23 nodes): `ViewportLayoutExporter.cs`, `ViewportLayoutExporter`, `.AlignOleToTargetMinPoint()`, `.ApplyWcsSize()`, `.BuildTargetRectangle()`, `.BuildTempPath()`, `.CollectRasterImages()`, `.EmbedSingleRasterAsync()`, `.EraseBlockContents()`, `.ExportToTempAsync()`, `.FindNewOle2Frame()`, `.GetModelSpaceSnapshot()`, `.IsCloseToTarget()`, `.ResizeOleToTarget()`, `.ResolveRasterPath()`, `.RunOleEmbeddingAsync()`, `.TryApplyPositionFallback()`, `.TryCloseDocument()`, `.TryCopyImageToClipboard()`, `.TryGetClipboardData()`, `.TryRestoreActiveDocument()`, `.TryRestoreClipboardData()`, `.Warn()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 5`** (13 nodes): `SmartTextCommands.cs`, `SmartTextCommands`, `.AreHeightsClose()`, `.AreTextsClose()`, `.CollectTextElements()`, `.CombineGroupText()`, `.EscapeMTextContent()`, `.EstimateTextWidth()`, `.GetTextBoundsAlongAxis()`, `.LowerBoundByPerp()`, `.ProjectPerpendicular()`, `.SmartGroupText()`, `.SmartMergeModelText()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 6`** (12 nodes): `TransmittalCommands.cs`, `TransmittalCommands`, `.ConfigureTransmittalInfo()`, `.ConvertMemberValue()`, `.CreateETransmitZip()`, `.PrepareOutputFolders()`, `.SafeGetTypes()`, `.SetMemberValue()`, `.TryCreateTransmittalOperation()`, `.TryDeleteTempFolder()`, `.TryLoadAssemblyByName()`, `.TryLoadAssemblyByPath()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 7`** (11 nodes): `TextStyleCommands.cs`, `TextStyleCommands`, `.BuildSignature()`, `.ChooseMasterStyle()`, `.CollectStyles()`, `.DeleteStyles()`, `.MergeTextStyles()`, `.Normalize()`, `.ReassignBlockAttributes()`, `.ReassignStyles()`, `.ReassignStylesInBlock()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 8`** (9 nodes): `StyleExportCommands.cs`, `StyleExportUtils.cs`, `StyleExportCommands`, `.ExportDimStyles()`, `.ExportTextStyles()`, `StyleExportUtils`, `.DumpProperties()`, `.ExportSymbolTableToMd()`, `.GetPropertyDisplayName()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 10`** (7 nodes): `ViewportCollector.cs`, `ViewportCollector`, `.Collect()`, `.ComputeModelWindow()`, `.GetDcsToWcsMatrix()`, `.GetViewCenterWcs()`, `.ResolveScale()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 12`** (5 nodes): `ButtonCommandHandler.cs`, `ICommand`, `ButtonCommandHandler`, `.CanExecute()`, `.Execute()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 13`** (5 nodes): `WindowsNaturalComparer.cs`, `IComparer`, `WindowsNaturalComparer`, `.Compare()`, `.StrCmpLogicalW()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `ViewportLayoutExporter` connect `Community 3` to `Community 0`?**
  _High betweenness centrality (0.042) - this node is a cross-community bridge._
- **What connects `LineInfo` to the rest of the system?**
  _1 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.1 - nodes in this community are weakly interconnected._
- **Should `Community 1` be split into smaller, more focused modules?**
  _Cohesion score 0.11 - nodes in this community are weakly interconnected._
- **Should `Community 2` be split into smaller, more focused modules?**
  _Cohesion score 0.11 - nodes in this community are weakly interconnected._
- **Should `Community 4` be split into smaller, more focused modules?**
  _Cohesion score 0.13 - nodes in this community are weakly interconnected._