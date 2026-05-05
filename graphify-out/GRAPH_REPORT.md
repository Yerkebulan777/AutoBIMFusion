# Graph Report - AutoBIMFusion  (2026-05-05)

## Corpus Check
- 39 files · ~17,299 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 241 nodes · 401 edges · 16 communities detected
- Extraction: 77% EXTRACTED · 23% INFERRED · 0% AMBIGUOUS · INFERRED: 94 edges (avg confidence: 0.8)
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
1. `DimensionStyleNormalizer` - 14 edges
2. `ExtentsUtils` - 12 edges
3. `LayoutProjectionProcessor` - 12 edges
4. `SmartTextCommands` - 12 edges
5. `TransmittalCommands` - 11 edges
6. `TextStyleCommands` - 10 edges
7. `ViewportTransformer` - 9 edges
8. `DimensionStyleDiagnosticUtils` - 8 edges
9. `DimensionUtils` - 8 edges
10. `LoggerFactory` - 8 edges

## Surprising Connections (you probably didn't know these)
- None detected - all connections are within the same source files.

## Communities

### Community 0 - "Community 0"
Cohesion: 0.1
Nodes (6): LayoutProjectionProcessor, ScaleCollector, ModelSpaceTrimmer, PickMainViewport(), ViewportTransformer, LayoutUtil

### Community 1 - "Community 1"
Cohesion: 0.13
Nodes (4): CombineStatistics, DwgOptimizer, CombineCommands, UiDialogService

### Community 2 - "Community 2"
Cohesion: 0.13
Nodes (7): BlockInserter, CombineOrchestrator, Fail(), Ok(), Warn(), ExtentsUtils, StringUtils

### Community 3 - "Community 3"
Cohesion: 0.13
Nodes (5): AutoBIMFusionExtension, RasterImagePathFixer, IExtensionApplication, RibbonBuilder, RibbonIconLoader

### Community 4 - "Community 4"
Cohesion: 0.26
Nodes (1): DimensionStyleNormalizer

### Community 5 - "Community 5"
Cohesion: 0.2
Nodes (3): JoinCommands, LineInfo, DimensionUtils

### Community 6 - "Community 6"
Cohesion: 0.28
Nodes (1): SmartTextCommands

### Community 7 - "Community 7"
Cohesion: 0.29
Nodes (1): TransmittalCommands

### Community 8 - "Community 8"
Cohesion: 0.27
Nodes (2): ViewportCollector, ViewportLayoutExporter

### Community 9 - "Community 9"
Cohesion: 0.33
Nodes (1): TextStyleCommands

### Community 10 - "Community 10"
Cohesion: 0.31
Nodes (3): ILogEventSink, DiagnosticSink, LoggerFactory

### Community 11 - "Community 11"
Cohesion: 0.24
Nodes (5): AcadUnitScalingOverrideScope, AcadWarningSuppressScope, SysVarScope, IDisposable, CloneTransformResult

### Community 12 - "Community 12"
Cohesion: 0.44
Nodes (1): DimensionStyleDiagnosticUtils

### Community 13 - "Community 13"
Cohesion: 0.28
Nodes (2): DrawOrderPreserver, EntityTransformUtils

### Community 14 - "Community 14"
Cohesion: 0.25
Nodes (3): IComparer, FileUtil, WindowsNaturalComparer

### Community 15 - "Community 15"
Cohesion: 0.4
Nodes (2): ICommand, ButtonCommandHandler

## Knowledge Gaps
- **1 isolated node(s):** `LineInfo`
  These have ≤1 connection - possible missing edges or undocumented components.
- **Thin community `Community 4`** (16 nodes): `DimensionStyleNormalizer.cs`, `.ToString()`, `DimensionStyleNormalizer`, `.BuildScaledStyleName()`, `.BuildStyleCache()`, `.CreateScaledStyle()`, `.FormatScale()`, `.FormatValue()`, `.IsUsableMultiplier()`, `.NormalizeDimensions()`, `.NormalizeStyleVisualScale()`, `.PurgeUnusedReplacedStyles()`, `.ResolveMultiplier()`, `.ResolveVisualBakeMultiplier()`, `.ScaleVisualValue()`, `.TryResolveStyleRecord()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 6`** (13 nodes): `SmartTextCommands.cs`, `SmartTextCommands`, `.AreHeightsClose()`, `.AreTextsClose()`, `.CollectTextElements()`, `.CombineGroupText()`, `.EscapeMTextContent()`, `.EstimateTextWidth()`, `.GetTextBoundsAlongAxis()`, `.LowerBoundByPerp()`, `.ProjectPerpendicular()`, `.SmartGroupText()`, `.SmartMergeModelText()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 7`** (12 nodes): `TransmittalCommands.cs`, `TransmittalCommands`, `.ConfigureTransmittalInfo()`, `.ConvertMemberValue()`, `.CreateETransmitZip()`, `.PrepareOutputFolders()`, `.SafeGetTypes()`, `.SetMemberValue()`, `.TryCreateTransmittalOperation()`, `.TryDeleteTempFolder()`, `.TryLoadAssemblyByName()`, `.TryLoadAssemblyByPath()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 8`** (11 nodes): `ViewportCollector.cs`, `ViewportLayoutExporter.cs`, `ViewportCollector`, `.Collect()`, `.ComputeModelWindow()`, `.GetDcsToWcsMatrix()`, `.GetViewCenterWcs()`, `.ResolveScale()`, `ViewportLayoutExporter`, `.PrepareDatabaseForMerge()`, `.TryFindFirstLayout()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 9`** (11 nodes): `TextStyleCommands.cs`, `TextStyleCommands`, `.BuildSignature()`, `.ChooseMasterStyle()`, `.CollectStyles()`, `.DeleteStyles()`, `.MergeTextStyles()`, `.Normalize()`, `.ReassignBlockAttributes()`, `.ReassignStyles()`, `.ReassignStylesInBlock()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 12`** (9 nodes): `DimensionStyleDiagnosticUtils.cs`, `DimensionStyleDiagnosticUtils`, `.Escape()`, `.F()`, `.FormatColor()`, `.FormatDimensionStyle()`, `.FormatTextStyle()`, `.IsUserStyle()`, `.LogStyleSnapshot()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 13`** (9 nodes): `DrawOrderPreserver.cs`, `EntityTransformUtils.cs`, `DrawOrderPreserver`, `.Capture()`, `.Restore()`, `EntityTransformUtils`, `.EvaluateHatch()`, `.TransformEntity()`, `.DeepCloneAndTransform()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 15`** (5 nodes): `ButtonCommandHandler.cs`, `ICommand`, `ButtonCommandHandler`, `.CanExecute()`, `.Execute()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `ViewportTransformer` connect `Community 0` to `Community 11`, `Community 13`?**
  _High betweenness centrality (0.044) - this node is a cross-community bridge._
- **Why does `LoggerFactory` connect `Community 10` to `Community 5`?**
  _High betweenness centrality (0.044) - this node is a cross-community bridge._
- **What connects `LineInfo` to the rest of the system?**
  _1 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.1 - nodes in this community are weakly interconnected._
- **Should `Community 1` be split into smaller, more focused modules?**
  _Cohesion score 0.13 - nodes in this community are weakly interconnected._
- **Should `Community 2` be split into smaller, more focused modules?**
  _Cohesion score 0.13 - nodes in this community are weakly interconnected._
- **Should `Community 3` be split into smaller, more focused modules?**
  _Cohesion score 0.13 - nodes in this community are weakly interconnected._