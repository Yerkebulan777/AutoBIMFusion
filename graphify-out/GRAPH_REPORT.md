# Graph Report - AutoBIMFusion  (2026-04-28)

## Corpus Check
- 45 files · ~23,568 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 240 nodes · 452 edges · 16 communities detected
- Extraction: 65% EXTRACTED · 35% INFERRED · 0% AMBIGUOUS · INFERRED: 159 edges (avg confidence: 0.8)
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

## God Nodes (most connected - your core abstractions)
1. `ViewportLayoutExporter` - 25 edges
2. `ExtentsUtils` - 18 edges
3. `ViewportTransformer` - 12 edges
4. `TransmittalCommands` - 11 edges
5. `TextStyleCommands` - 10 edges
6. `SmartTextCommands` - 9 edges
7. `MergeCommands` - 8 edges
8. `OperationLogger` - 7 edges
9. `ViewportCollector` - 6 edges
10. `MergeStatistics` - 6 edges

## Surprising Connections (you probably didn't know these)
- None detected - all connections are within the same source files.

## Communities

### Community 0 - "Community 0"
Cohesion: 0.14
Nodes (4): DrawOrderPreserver, ModelSpaceTrimmer, ViewportTransformer, PickMainViewport()

### Community 1 - "Community 1"
Cohesion: 0.11
Nodes (7): JoinCommands, LineInfo, MergeCommands, DwgOptimizer, FileEnumerator, FolderSelector, OperationLogger

### Community 2 - "Community 2"
Cohesion: 0.16
Nodes (2): ViewportLayoutExporter, LayoutUtil

### Community 3 - "Community 3"
Cohesion: 0.11
Nodes (7): FileHelper, MergeCoordinator, Fail(), Ok(), Warn(), MergeStatistics, StringUtils

### Community 4 - "Community 4"
Cohesion: 0.14
Nodes (2): ExtentsUtils, BlockInserter

### Community 5 - "Community 5"
Cohesion: 0.12
Nodes (5): AutoBIMFusionExtension, IExtensionApplication, RasterImagePathFixer, RibbonBuilder, RibbonIconLoader

### Community 6 - "Community 6"
Cohesion: 0.33
Nodes (1): TextStyleCommands

### Community 7 - "Community 7"
Cohesion: 0.31
Nodes (1): TransmittalCommands

### Community 8 - "Community 8"
Cohesion: 0.36
Nodes (1): SmartTextCommands

### Community 9 - "Community 9"
Cohesion: 0.31
Nodes (2): StyleExportCommands, StyleExportUtils

### Community 10 - "Community 10"
Cohesion: 0.39
Nodes (4): AcadWarningSuppressScope, LayoutEditScope, ManagedSystemVariable, IDisposable

### Community 11 - "Community 11"
Cohesion: 0.52
Nodes (1): ViewportCollector

### Community 12 - "Community 12"
Cohesion: 0.29
Nodes (3): ILogEventSink, DiagnosticSink, LoggerFactory

### Community 13 - "Community 13"
Cohesion: 0.4
Nodes (2): ICommand, ButtonCommandHandler

### Community 14 - "Community 14"
Cohesion: 0.5
Nodes (2): IComparer, WindowsNaturalComparer

### Community 15 - "Community 15"
Cohesion: 0.67
Nodes (1): Program

## Knowledge Gaps
- **1 isolated node(s):** `LineInfo`
  These have ≤1 connection - possible missing edges or undocumented components.
- **Thin community `Community 2`** (27 nodes): `ViewportLayoutExporter.cs`, `LayoutUtil.cs`, `ViewportLayoutExporter`, `.AlignOleToTargetMinPoint()`, `.ApplyWcsSize()`, `.BuildTargetRectangle()`, `.BuildTempPath()`, `.CheckIfNeedsOle()`, `.CollectRasterImages()`, `.EmbedSingleRasterAsync()`, `.EraseBlockContents()`, `.ExportToTempAsync()`, `.FindNewOle2Frame()`, `.GetModelSpaceSnapshot()`, `.IsCloseToTarget()`, `.MovePaperToModelSpace()`, `.ProcessNoVp()`, `.ResizeOleToTarget()`, `.ResolveRasterPath()`, `.RunOleEmbeddingAsync()`, `.TryApplyPositionFallback()`, `.TryCopyImageToClipboard()`, `LayoutUtil`, `.GetLayoutBtrId()`, `.GetPaperSpaceEntities()`, `.TryFindFirstLayout()`, `.Warn()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 4`** (20 nodes): `BlockInserter.cs`, `ExtentsUtils.cs`, `ExtentsUtils`, `.Expand()`, `.GetArea()`, `.GetCenter()`, `.GetExtents()`, `.GetExtents2d()`, `.GetVolume()`, `.IsEntityPointIn()`, `.IsPointIn()`, `.ToExtents2d()`, `.ToExtents3d()`, `.Transform()`, `.Union()`, `BlockInserter`, `.CalcInsertionPoint()`, `.CreateDimensionSnapshot()`, `.InsertNativeObjects()`, `.ResolveDimensionStyleName()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 6`** (11 nodes): `TextStyleCommands.cs`, `TextStyleCommands`, `.BuildSignature()`, `.ChooseMasterStyle()`, `.CollectStyles()`, `.DeleteStyles()`, `.MergeTextStyles()`, `.Normalize()`, `.ReassignBlockAttributes()`, `.ReassignStyles()`, `.ReassignStylesInBlock()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 7`** (11 nodes): `TransmittalCommands.cs`, `TransmittalCommands`, `.ConfigureTransmittalInfo()`, `.ConvertMemberValue()`, `.CreateETransmitZip()`, `.PrepareOutputFolders()`, `.SafeGetTypes()`, `.SetMemberValue()`, `.TryCreateTransmittalOperation()`, `.TryDeleteTempFolder()`, `.TryLoadAssemblyByPath()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 8`** (10 nodes): `SmartTextCommands.cs`, `SmartTextCommands`, `.AreTextsClose()`, `.CollectTextElements()`, `.CombineGroupText()`, `.EscapeMTextContent()`, `.EstimateTextWidth()`, `.GetTextBoundsAlongAxis()`, `.SmartGroupText()`, `.SmartMergeModelText()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 9`** (9 nodes): `StyleExportCommands.cs`, `StyleExportUtils.cs`, `StyleExportCommands`, `.ExportDimStyles()`, `.ExportTextStyles()`, `StyleExportUtils`, `.DumpProperties()`, `.ExportSymbolTableToMd()`, `.GetPropertyDisplayName()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 11`** (7 nodes): `ViewportCollector.cs`, `ViewportCollector`, `.Collect()`, `.ComputeModelWindow()`, `.GetDcsToWcsMatrix()`, `.GetViewCenterWcs()`, `.ResolveScale()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 13`** (5 nodes): `ButtonCommandHandler.cs`, `ICommand`, `ButtonCommandHandler`, `.CanExecute()`, `.Execute()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 14`** (5 nodes): `WindowsNaturalComparer.cs`, `IComparer`, `WindowsNaturalComparer`, `.Compare()`, `.StrCmpLogicalW()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 15`** (3 nodes): `Program`, `.Main()`, `fix_enc.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `ExtentsUtils` connect `Community 4` to `Community 0`, `Community 3`?**
  _High betweenness centrality (0.066) - this node is a cross-community bridge._
- **Why does `ViewportLayoutExporter` connect `Community 2` to `Community 0`?**
  _High betweenness centrality (0.036) - this node is a cross-community bridge._
- **What connects `LineInfo` to the rest of the system?**
  _1 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.14 - nodes in this community are weakly interconnected._
- **Should `Community 1` be split into smaller, more focused modules?**
  _Cohesion score 0.11 - nodes in this community are weakly interconnected._
- **Should `Community 3` be split into smaller, more focused modules?**
  _Cohesion score 0.11 - nodes in this community are weakly interconnected._
- **Should `Community 4` be split into smaller, more focused modules?**
  _Cohesion score 0.14 - nodes in this community are weakly interconnected._