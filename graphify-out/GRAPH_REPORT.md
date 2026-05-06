# Graph Report - AutoBIMFusion  (2026-05-06)

## Corpus Check
- 39 files · ~18,452 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 264 nodes · 458 edges · 15 communities detected
- Extraction: 79% EXTRACTED · 21% INFERRED · 0% AMBIGUOUS · INFERRED: 98 edges (avg confidence: 0.8)
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
1. `DimensionStyleNormalizer` - 30 edges
2. `DimensionStyleDiagnosticUtils` - 14 edges
3. `ExtentsUtils` - 12 edges
4. `LayoutProjectionProcessor` - 12 edges
5. `SmartTextCommands` - 12 edges
6. `TransmittalCommands` - 11 edges
7. `DimensionUtils` - 10 edges
8. `TextStyleCommands` - 10 edges
9. `ViewportTransformer` - 9 edges
10. `CombineCommands` - 7 edges

## Surprising Connections (you probably didn't know these)
- None detected - all connections are within the same source files.

## Communities

### Community 0 - "Community 0"
Cohesion: 0.08
Nodes (8): BlockInserter, ExtentsUtils, LayoutProjectionProcessor, ScaleCollector, ModelSpaceTrimmer, PickMainViewport(), ViewportTransformer, LayoutUtil

### Community 1 - "Community 1"
Cohesion: 0.14
Nodes (1): DimensionStyleNormalizer

### Community 2 - "Community 2"
Cohesion: 0.12
Nodes (5): CombineStatistics, RasterImagePathFixer, CombineCommands, RibbonIconLoader, UiDialogService

### Community 3 - "Community 3"
Cohesion: 0.13
Nodes (7): AcadUnitScalingOverrideScope, AcadWarningSuppressScope, SysVarScope, IDisposable, ViewportCollector, ViewportLayoutExporter, CloneTransformResult

### Community 4 - "Community 4"
Cohesion: 0.13
Nodes (8): CombineOrchestrator, Fail(), Ok(), Warn(), IComparer, StringUtils, FileUtil, WindowsNaturalComparer

### Community 5 - "Community 5"
Cohesion: 0.18
Nodes (3): JoinCommands, LineInfo, DimensionUtils

### Community 6 - "Community 6"
Cohesion: 0.3
Nodes (1): DimensionStyleDiagnosticUtils

### Community 7 - "Community 7"
Cohesion: 0.28
Nodes (1): SmartTextCommands

### Community 8 - "Community 8"
Cohesion: 0.29
Nodes (1): TransmittalCommands

### Community 9 - "Community 9"
Cohesion: 0.33
Nodes (1): TextStyleCommands

### Community 10 - "Community 10"
Cohesion: 0.22
Nodes (3): AutoBIMFusionExtension, IExtensionApplication, RibbonBuilder

### Community 11 - "Community 11"
Cohesion: 0.31
Nodes (3): ILogEventSink, DiagnosticSink, LoggerFactory

### Community 12 - "Community 12"
Cohesion: 0.28
Nodes (2): DrawOrderPreserver, EntityTransformUtils

### Community 13 - "Community 13"
Cohesion: 0.48
Nodes (1): DwgOptimizer

### Community 14 - "Community 14"
Cohesion: 0.4
Nodes (2): ICommand, ButtonCommandHandler

## Knowledge Gaps
- **1 isolated node(s):** `LineInfo`
  These have ≤1 connection - possible missing edges or undocumented components.
- **Thin community `Community 1`** (32 nodes): `DimensionStyleNormalizer.cs`, `.ToString()`, `DimensionStyleNormalizer`, `.AddAnnotativeStyleIds()`, `.AddReplacedStyle()`, `.ApplyMetricStyle()`, `.BuildMetricStyleName()`, `.BuildStyleCache()`, `.CountMultiplierFallbacks()`, `.CountSkippedStyle()`, `.CountStillReferencedStyles()`, `.CountStyleCreation()`, `.CreateMetricStyle()`, `.EraseLayoutAnnotation()`, `.FormatChange()`, `.FormatObjectId()`, `.FormatScale()`, `.FormatStyleVisualChanges()`, `.FormatValue()`, `.IsAnnotativeStyle()`, `.IsStandardStyle()`, `.IsUsableMultiplier()`, `.NormalizeStyleVisualScale()`, `.PurgeUnusedStyles()`, `.RecreateMetricDimStyles()`, `.ResolveMultiplier()`, `.ResolveVisualBakeMultiplier()`, `.ScaleVisualValue()`, `.TryGetMetricStyleForDimension()`, `.TryGetMetricStyleForLeader()`, `.TryGetOrCreateMetricStyle()`, `.TryResolveStyleRecord()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 6`** (15 nodes): `DimensionStyleDiagnosticUtils.cs`, `DimensionStyleDiagnosticUtils`, `.AddUsedDimensionStyleId()`, `.CollectUsedDimensionStyleIds()`, `.Escape()`, `.F()`, `.FormatColor()`, `.FormatDimensionStyle()`, `.FormatObjectId()`, `.FormatPropertyValue()`, `.FormatTextStyle()`, `.FormatValue()`, `.LogStyleSnapshot()`, `.ReadOptionalBool()`, `.ShouldLogDimensionStyle()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 7`** (13 nodes): `SmartTextCommands`, `.AreHeightsClose()`, `.AreTextsClose()`, `.CollectTextElements()`, `.CombineGroupText()`, `.EscapeMTextContent()`, `.EstimateTextWidth()`, `.GetTextBoundsAlongAxis()`, `.LowerBoundByPerp()`, `.ProjectPerpendicular()`, `.SmartGroupText()`, `.SmartMergeModelText()`, `SmartTextCommands.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 8`** (12 nodes): `TransmittalCommands`, `.ConfigureTransmittalInfo()`, `.ConvertMemberValue()`, `.CreateETransmitZip()`, `.PrepareOutputFolders()`, `.SafeGetTypes()`, `.SetMemberValue()`, `.TryCreateTransmittalOperation()`, `.TryDeleteTempFolder()`, `.TryLoadAssemblyByName()`, `.TryLoadAssemblyByPath()`, `TransmittalCommands.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 9`** (11 nodes): `TextStyleCommands`, `.BuildSignature()`, `.ChooseMasterStyle()`, `.CollectStyles()`, `.DeleteStyles()`, `.MergeTextStyles()`, `.Normalize()`, `.ReassignBlockAttributes()`, `.ReassignStyles()`, `.ReassignStylesInBlock()`, `TextStyleCommands.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 12`** (9 nodes): `DrawOrderPreserver.cs`, `EntityTransformUtils.cs`, `DrawOrderPreserver`, `.Capture()`, `.Restore()`, `EntityTransformUtils`, `.EvaluateHatch()`, `.TransformEntity()`, `.DeepCloneAndTransform()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 13`** (7 nodes): `DwgOptimizer.cs`, `DwgOptimizer`, `.AddDictionaryIds()`, `.AddTableIds()`, `.ErasePurgedObjects()`, `.Optimize()`, `.PurgePass()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 14`** (5 nodes): `ButtonCommandHandler.cs`, `ICommand`, `ButtonCommandHandler`, `.CanExecute()`, `.Execute()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `ViewportTransformer` connect `Community 0` to `Community 3`, `Community 12`?**
  _High betweenness centrality (0.048) - this node is a cross-community bridge._
- **What connects `LineInfo` to the rest of the system?**
  _1 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.08 - nodes in this community are weakly interconnected._
- **Should `Community 1` be split into smaller, more focused modules?**
  _Cohesion score 0.14 - nodes in this community are weakly interconnected._
- **Should `Community 2` be split into smaller, more focused modules?**
  _Cohesion score 0.12 - nodes in this community are weakly interconnected._
- **Should `Community 3` be split into smaller, more focused modules?**
  _Cohesion score 0.13 - nodes in this community are weakly interconnected._
- **Should `Community 4` be split into smaller, more focused modules?**
  _Cohesion score 0.13 - nodes in this community are weakly interconnected._