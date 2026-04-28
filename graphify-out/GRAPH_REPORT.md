# Graph Report - AutoBIMFusion  (2026-04-28)

## Corpus Check
- 45 files · ~23,133 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 231 nodes · 431 edges · 18 communities detected
- Extraction: 64% EXTRACTED · 36% INFERRED · 0% AMBIGUOUS · INFERRED: 156 edges (avg confidence: 0.8)
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

## God Nodes (most connected - your core abstractions)
1. `ViewportLayoutExporter` - 23 edges
2. `ExtentsUtils` - 14 edges
3. `TransmittalCommands` - 11 edges
4. `TextStyleCommands` - 10 edges
5. `SmartTextCommands` - 9 edges
6. `ViewportTransformer` - 9 edges
7. `MergeCommands` - 8 edges
8. `OperationLogger` - 7 edges
9. `ViewportCollector` - 6 edges
10. `MergeStatistics` - 6 edges

## Surprising Connections (you probably didn't know these)
- None detected - all connections are within the same source files.

## Communities

### Community 0 - "Community 0"
Cohesion: 0.14
Nodes (3): ModelSpaceTrimmer, ViewportLayoutExporter, LayoutUtil

### Community 1 - "Community 1"
Cohesion: 0.19
Nodes (3): DrawOrderPreserver, ViewportTransformer, PickMainViewport()

### Community 2 - "Community 2"
Cohesion: 0.13
Nodes (7): FileHelper, MergeCoordinator, Fail(), Ok(), Warn(), OperationLogger, StringUtils

### Community 3 - "Community 3"
Cohesion: 0.16
Nodes (3): MergeCommands, FolderSelector, MergeStatistics

### Community 4 - "Community 4"
Cohesion: 0.13
Nodes (5): AutoBIMFusionExtension, IExtensionApplication, RasterImagePathFixer, RibbonBuilder, RibbonIconLoader

### Community 5 - "Community 5"
Cohesion: 0.18
Nodes (2): ExtentsUtils, BlockInserter

### Community 6 - "Community 6"
Cohesion: 0.29
Nodes (1): TransmittalCommands

### Community 7 - "Community 7"
Cohesion: 0.33
Nodes (1): TextStyleCommands

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
Cohesion: 0.29
Nodes (3): FileEnumerator, IComparer, WindowsNaturalComparer

### Community 12 - "Community 12"
Cohesion: 0.52
Nodes (1): ViewportCollector

### Community 13 - "Community 13"
Cohesion: 0.29
Nodes (3): ILogEventSink, DiagnosticSink, LoggerFactory

### Community 14 - "Community 14"
Cohesion: 0.53
Nodes (1): DwgOptimizer

### Community 15 - "Community 15"
Cohesion: 0.5
Nodes (2): JoinCommands, LineInfo

### Community 16 - "Community 16"
Cohesion: 0.4
Nodes (2): ICommand, ButtonCommandHandler

### Community 17 - "Community 17"
Cohesion: 0.67
Nodes (1): Program

## Knowledge Gaps
- **1 isolated node(s):** `LineInfo`
  These have ≤1 connection - possible missing edges or undocumented components.
- **Thin community `Community 5`** (17 nodes): `BlockInserter.cs`, `ExtentsUtils.cs`, `ExtentsUtils`, `.GetCenter()`, `.GetExtents()`, `.GetExtents2d()`, `.IsEntityPointIn()`, `.IsPointIn()`, `.ToExtents2d()`, `.Transform()`, `.TryGetExtents()`, `.Union()`, `BlockInserter`, `.CalcInsertionPoint()`, `.CreateDimensionSnapshot()`, `.InsertNativeObjects()`, `.ResolveDimensionStyleName()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 6`** (12 nodes): `TransmittalCommands.cs`, `TransmittalCommands`, `.ConfigureTransmittalInfo()`, `.ConvertMemberValue()`, `.CreateETransmitZip()`, `.PrepareOutputFolders()`, `.SafeGetTypes()`, `.SetMemberValue()`, `.TryCreateTransmittalOperation()`, `.TryDeleteTempFolder()`, `.TryLoadAssemblyByName()`, `.TryLoadAssemblyByPath()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 7`** (11 nodes): `TextStyleCommands.cs`, `TextStyleCommands`, `.BuildSignature()`, `.ChooseMasterStyle()`, `.CollectStyles()`, `.DeleteStyles()`, `.MergeTextStyles()`, `.Normalize()`, `.ReassignBlockAttributes()`, `.ReassignStyles()`, `.ReassignStylesInBlock()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 8`** (10 nodes): `SmartTextCommands.cs`, `SmartTextCommands`, `.AreTextsClose()`, `.CollectTextElements()`, `.CombineGroupText()`, `.EscapeMTextContent()`, `.EstimateTextWidth()`, `.GetTextBoundsAlongAxis()`, `.SmartGroupText()`, `.SmartMergeModelText()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 9`** (9 nodes): `StyleExportCommands.cs`, `StyleExportUtils.cs`, `StyleExportCommands`, `.ExportDimStyles()`, `.ExportTextStyles()`, `StyleExportUtils`, `.DumpProperties()`, `.ExportSymbolTableToMd()`, `.GetPropertyDisplayName()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 12`** (7 nodes): `ViewportCollector.cs`, `ViewportCollector`, `.Collect()`, `.ComputeModelWindow()`, `.GetDcsToWcsMatrix()`, `.GetViewCenterWcs()`, `.ResolveScale()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 14`** (6 nodes): `DwgOptimizer.cs`, `DwgOptimizer`, `.AddDictionaryIds()`, `.AddTableIds()`, `.Optimize()`, `.PurgePass()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 15`** (5 nodes): `JoinCommands.cs`, `JoinCommands`, `.JoinLinesCommand()`, `.MergeGroup()`, `LineInfo`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 16`** (5 nodes): `ButtonCommandHandler.cs`, `ICommand`, `ButtonCommandHandler`, `.CanExecute()`, `.Execute()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 17`** (3 nodes): `Program`, `.Main()`, `fix_enc.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `ExtentsUtils` connect `Community 5` to `Community 1`, `Community 2`?**
  _High betweenness centrality (0.039) - this node is a cross-community bridge._
- **Why does `ViewportLayoutExporter` connect `Community 0` to `Community 1`?**
  _High betweenness centrality (0.037) - this node is a cross-community bridge._
- **What connects `LineInfo` to the rest of the system?**
  _1 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.14 - nodes in this community are weakly interconnected._
- **Should `Community 2` be split into smaller, more focused modules?**
  _Cohesion score 0.13 - nodes in this community are weakly interconnected._
- **Should `Community 4` be split into smaller, more focused modules?**
  _Cohesion score 0.13 - nodes in this community are weakly interconnected._