# Graph Report - AutoBIMFusion  (2026-04-30)

## Corpus Check
- 48 files · ~18,680 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 249 nodes · 465 edges · 18 communities detected
- Extraction: 65% EXTRACTED · 35% INFERRED · 0% AMBIGUOUS · INFERRED: 165 edges (avg confidence: 0.8)
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
1. `DimensionStyleDiagnosticUtils` - 18 edges
2. `SmartTextCommands` - 12 edges
3. `TransmittalCommands` - 11 edges
4. `ExtentsUtils` - 11 edges
5. `TextStyleCommands` - 10 edges
6. `ViewportTransformer` - 10 edges
7. `MergeCommands` - 9 edges
8. `LayoutProjectionProcessor` - 9 edges
9. `AILog` - 7 edges
10. `ViewportCollector` - 6 edges

## Surprising Connections (you probably didn't know these)
- None detected - all connections are within the same source files.

## Communities

### Community 0 - "Community 0"
Cohesion: 0.11
Nodes (6): DrawOrderPreserver, LayoutProjectionProcessor, ModelSpaceTrimmer, ViewportTransformer, PickMainViewport(), EntityTransformUtils

### Community 1 - "Community 1"
Cohesion: 0.16
Nodes (3): MergeCommands, MergeStatistics, UiDialogService

### Community 2 - "Community 2"
Cohesion: 0.2
Nodes (1): DimensionStyleDiagnosticUtils

### Community 3 - "Community 3"
Cohesion: 0.13
Nodes (5): AutoBIMFusionExtension, IExtensionApplication, RasterImagePathFixer, RibbonBuilder, RibbonIconLoader

### Community 4 - "Community 4"
Cohesion: 0.21
Nodes (2): TransmittalCommands, AILog

### Community 5 - "Community 5"
Cohesion: 0.16
Nodes (6): MergeCoordinator, Fail(), Ok(), Warn(), StringUtils, FileHelper

### Community 6 - "Community 6"
Cohesion: 0.21
Nodes (3): ViewportCollector, ViewportLayoutExporter, LayoutUtil

### Community 7 - "Community 7"
Cohesion: 0.28
Nodes (1): SmartTextCommands

### Community 8 - "Community 8"
Cohesion: 0.33
Nodes (1): TextStyleCommands

### Community 9 - "Community 9"
Cohesion: 0.24
Nodes (2): ExtentsUtils, BlockInserter

### Community 10 - "Community 10"
Cohesion: 0.31
Nodes (2): StyleExportCommands, StyleExportUtils

### Community 11 - "Community 11"
Cohesion: 0.31
Nodes (3): ILogEventSink, DiagnosticSink, LoggerFactory

### Community 12 - "Community 12"
Cohesion: 0.29
Nodes (3): IComparer, FileEnumerator, WindowsNaturalComparer

### Community 13 - "Community 13"
Cohesion: 0.47
Nodes (3): AcadWarningSuppressScope, ManagedSystemVariable, IDisposable

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
Nodes (1): FolderSelector

## Knowledge Gaps
- **1 isolated node(s):** `LineInfo`
  These have ≤1 connection - possible missing edges or undocumented components.
- **Thin community `Community 2`** (20 nodes): `DimensionStyleDiagnosticUtils.cs`, `DimensionStyleDiagnosticUtils`, `.ClearDimensionOverrides()`, `.CreateSnapshot()`, `.Escape()`, `.FormatColor()`, `.FormatDimensionStyle()`, `.FormatDouble()`, `.FormatSnapshot()`, `.FormatTextStyle()`, `.IsControlString()`, `.IsDimensionStyleOverrideMarker()`, `.IsRegApp()`, `.IsUserStyle()`, `.LogStyleSnapshot()`, `.SkipOverridePayload()`, `.TryRemoveAcadDimensionStyleOverrideSection()`, `.TryRemoveDimensionStyleOverrides()`, `.TryRemoveDimensionStyleOverrideSection()`, `.ToString()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 4`** (17 nodes): `TransmittalCommands.cs`, `AILog.cs`, `TransmittalCommands`, `.ConfigureTransmittalInfo()`, `.ConvertMemberValue()`, `.CreateETransmitZip()`, `.PrepareOutputFolders()`, `.SafeGetTypes()`, `.SetMemberValue()`, `.TryCreateTransmittalOperation()`, `.TryDeleteTempFolder()`, `.TryLoadAssemblyByName()`, `.TryLoadAssemblyByPath()`, `AILog`, `.Log()`, `.TryWriteToEditor()`, `.Warn()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 7`** (13 nodes): `SmartTextCommands.cs`, `SmartTextCommands`, `.AreHeightsClose()`, `.AreTextsClose()`, `.CollectTextElements()`, `.CombineGroupText()`, `.EscapeMTextContent()`, `.EstimateTextWidth()`, `.GetTextBoundsAlongAxis()`, `.LowerBoundByPerp()`, `.ProjectPerpendicular()`, `.SmartGroupText()`, `.SmartMergeModelText()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 8`** (11 nodes): `TextStyleCommands.cs`, `TextStyleCommands`, `.BuildSignature()`, `.ChooseMasterStyle()`, `.CollectStyles()`, `.DeleteStyles()`, `.MergeTextStyles()`, `.Normalize()`, `.ReassignBlockAttributes()`, `.ReassignStyles()`, `.ReassignStylesInBlock()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 9`** (11 nodes): `BlockInserter.cs`, `ExtentsUtils.cs`, `ExtentsUtils`, `.IsEntityPointIn()`, `.IsPointIn()`, `.Transform()`, `.TryGetScaleRatio()`, `.Union()`, `BlockInserter`, `.CalcInsertionPoint()`, `.InsertNativeObjects()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 10`** (9 nodes): `StyleExportCommands.cs`, `StyleExportUtils.cs`, `StyleExportCommands`, `.ExportDimStyles()`, `.ExportTextStyles()`, `StyleExportUtils`, `.DumpProperties()`, `.ExportSymbolTableToMd()`, `.GetPropertyDisplayName()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 14`** (6 nodes): `DwgOptimizer.cs`, `DwgOptimizer`, `.AddDictionaryIds()`, `.AddTableIds()`, `.Optimize()`, `.PurgePass()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 15`** (5 nodes): `JoinCommands.cs`, `JoinCommands`, `.JoinLinesCommand()`, `.MergeGroup()`, `LineInfo`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 16`** (5 nodes): `ButtonCommandHandler.cs`, `ICommand`, `ButtonCommandHandler`, `.CanExecute()`, `.Execute()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 17`** (3 nodes): `FolderSelector.cs`, `FolderSelector`, `.TrySelectFolder()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **What connects `LineInfo` to the rest of the system?**
  _1 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.11 - nodes in this community are weakly interconnected._
- **Should `Community 3` be split into smaller, more focused modules?**
  _Cohesion score 0.13 - nodes in this community are weakly interconnected._