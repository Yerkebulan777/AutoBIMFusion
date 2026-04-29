# Graph Report - AutoBIMFusion  (2026-04-29)

## Corpus Check
- 46 files · ~17,288 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 231 nodes · 423 edges · 18 communities detected
- Extraction: 63% EXTRACTED · 37% INFERRED · 0% AMBIGUOUS · INFERRED: 155 edges (avg confidence: 0.8)
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
1. `SmartTextCommands` - 12 edges
2. `TransmittalCommands` - 11 edges
3. `ExtentsUtils` - 11 edges
4. `TextStyleCommands` - 10 edges
5. `ViewportTransformer` - 10 edges
6. `LayoutProjectionProcessor` - 9 edges
7. `MergeCommands` - 8 edges
8. `DimensionTransformUtils` - 7 edges
9. `AILog` - 7 edges
10. `ViewportCollector` - 6 edges

## Surprising Connections (you probably didn't know these)
- None detected - all connections are within the same source files.

## Communities

### Community 0 - "Community 0"
Cohesion: 0.18
Nodes (3): LayoutProjectionProcessor, ViewportTransformer, PickMainViewport()

### Community 1 - "Community 1"
Cohesion: 0.17
Nodes (3): MergeCommands, FolderSelector, MergeStatistics

### Community 2 - "Community 2"
Cohesion: 0.18
Nodes (3): DrawOrderPreserver, EntityTransformUtils, AILog

### Community 3 - "Community 3"
Cohesion: 0.13
Nodes (5): AutoBIMFusionExtension, IExtensionApplication, RasterImagePathFixer, RibbonBuilder, RibbonIconLoader

### Community 4 - "Community 4"
Cohesion: 0.16
Nodes (6): MergeCoordinator, Fail(), Ok(), Warn(), StringUtils, FileHelper

### Community 5 - "Community 5"
Cohesion: 0.2
Nodes (3): ExtentsUtils, ModelSpaceTrimmer, BlockInserter

### Community 6 - "Community 6"
Cohesion: 0.21
Nodes (3): ViewportCollector, ViewportLayoutExporter, LayoutUtil

### Community 7 - "Community 7"
Cohesion: 0.28
Nodes (1): SmartTextCommands

### Community 8 - "Community 8"
Cohesion: 0.28
Nodes (1): TransmittalCommands

### Community 9 - "Community 9"
Cohesion: 0.33
Nodes (1): TextStyleCommands

### Community 10 - "Community 10"
Cohesion: 0.27
Nodes (2): StyleExportCommands, StyleExportUtils

### Community 11 - "Community 11"
Cohesion: 0.39
Nodes (4): AcadWarningSuppressScope, LayoutEditScope, ManagedSystemVariable, IDisposable

### Community 12 - "Community 12"
Cohesion: 0.46
Nodes (1): DimensionTransformUtils

### Community 13 - "Community 13"
Cohesion: 0.29
Nodes (3): IComparer, FileEnumerator, WindowsNaturalComparer

### Community 14 - "Community 14"
Cohesion: 0.29
Nodes (3): ILogEventSink, DiagnosticSink, LoggerFactory

### Community 15 - "Community 15"
Cohesion: 0.53
Nodes (1): DwgOptimizer

### Community 16 - "Community 16"
Cohesion: 0.5
Nodes (2): JoinCommands, LineInfo

### Community 17 - "Community 17"
Cohesion: 0.4
Nodes (2): ICommand, ButtonCommandHandler

## Knowledge Gaps
- **1 isolated node(s):** `LineInfo`
  These have ≤1 connection - possible missing edges or undocumented components.
- **Thin community `Community 7`** (13 nodes): `SmartTextCommands.cs`, `SmartTextCommands`, `.AreHeightsClose()`, `.AreTextsClose()`, `.CollectTextElements()`, `.CombineGroupText()`, `.EscapeMTextContent()`, `.EstimateTextWidth()`, `.GetTextBoundsAlongAxis()`, `.LowerBoundByPerp()`, `.ProjectPerpendicular()`, `.SmartGroupText()`, `.SmartMergeModelText()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 8`** (13 nodes): `TransmittalCommands.cs`, `TransmittalCommands`, `.ConfigureTransmittalInfo()`, `.ConvertMemberValue()`, `.CreateETransmitZip()`, `.PrepareOutputFolders()`, `.SafeGetTypes()`, `.SetMemberValue()`, `.TryCreateTransmittalOperation()`, `.TryDeleteTempFolder()`, `.TryLoadAssemblyByName()`, `.TryLoadAssemblyByPath()`, `.Warn()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 9`** (11 nodes): `TextStyleCommands.cs`, `TextStyleCommands`, `.BuildSignature()`, `.ChooseMasterStyle()`, `.CollectStyles()`, `.DeleteStyles()`, `.MergeTextStyles()`, `.Normalize()`, `.ReassignBlockAttributes()`, `.ReassignStyles()`, `.ReassignStylesInBlock()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 10`** (10 nodes): `StyleExportCommands.cs`, `StyleExportUtils.cs`, `StyleExportCommands`, `.ExportDimStyles()`, `.ExportTextStyles()`, `.ToString()`, `StyleExportUtils`, `.DumpProperties()`, `.ExportSymbolTableToMd()`, `.GetPropertyDisplayName()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 12`** (8 nodes): `DimensionTransformUtils.cs`, `DimensionTransformUtils`, `.AdjustDimensionScale()`, `.EscapeDiagnosticText()`, `.FormatDouble()`, `.LogDimensionDiagnostic()`, `.ReadDouble()`, `.TransformDimension()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 15`** (6 nodes): `DwgOptimizer.cs`, `DwgOptimizer`, `.AddDictionaryIds()`, `.AddTableIds()`, `.Optimize()`, `.PurgePass()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 16`** (5 nodes): `JoinCommands.cs`, `JoinCommands`, `.JoinLinesCommand()`, `.MergeGroup()`, `LineInfo`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 17`** (5 nodes): `ButtonCommandHandler.cs`, `ICommand`, `ButtonCommandHandler`, `.CanExecute()`, `.Execute()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **What connects `LineInfo` to the rest of the system?**
  _1 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 3` be split into smaller, more focused modules?**
  _Cohesion score 0.13 - nodes in this community are weakly interconnected._