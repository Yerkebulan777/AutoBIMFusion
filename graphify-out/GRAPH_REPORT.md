# Graph Report - AutoBIMFusion  (2026-05-05)

## Corpus Check
- 39 files · ~17,578 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 249 nodes · 425 edges · 17 communities detected
- Extraction: 77% EXTRACTED · 23% INFERRED · 0% AMBIGUOUS · INFERRED: 99 edges (avg confidence: 0.8)
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

## God Nodes (most connected - your core abstractions)
1. `DimensionStyleNormalizer` - 16 edges
2. `DimensionStyleDiagnosticUtils` - 14 edges
3. `ExtentsUtils` - 12 edges
4. `LayoutProjectionProcessor` - 12 edges
5. `SmartTextCommands` - 12 edges
6. `TransmittalCommands` - 11 edges
7. `TextStyleCommands` - 10 edges
8. `ViewportTransformer` - 9 edges
9. `DimensionUtils` - 8 edges
10. `LoggerFactory` - 8 edges

## Surprising Connections (you probably didn't know these)
- None detected - all connections are within the same source files.

## Communities

### Community 0 - "Community 0"
Cohesion: 0.11
Nodes (5): LayoutProjectionProcessor, ScaleCollector, ModelSpaceTrimmer, PickMainViewport(), ViewportTransformer

### Community 1 - "Community 1"
Cohesion: 0.09
Nodes (9): BlockInserter, Fail(), Ok(), Warn(), CombineOrchestrator, ExtentsUtils, StringUtils, LayoutUtil (+1 more)

### Community 2 - "Community 2"
Cohesion: 0.15
Nodes (3): RasterImagePathFixer, TransmittalCommands, RibbonIconLoader

### Community 3 - "Community 3"
Cohesion: 0.18
Nodes (3): CombineStatistics, CombineCommands, UiDialogService

### Community 4 - "Community 4"
Cohesion: 0.17
Nodes (3): JoinCommands, LineInfo, SmartTextCommands

### Community 5 - "Community 5"
Cohesion: 0.24
Nodes (1): DimensionStyleNormalizer

### Community 6 - "Community 6"
Cohesion: 0.28
Nodes (1): DimensionStyleDiagnosticUtils

### Community 7 - "Community 7"
Cohesion: 0.33
Nodes (1): TextStyleCommands

### Community 8 - "Community 8"
Cohesion: 0.31
Nodes (3): ILogEventSink, DiagnosticSink, LoggerFactory

### Community 9 - "Community 9"
Cohesion: 0.24
Nodes (5): AcadUnitScalingOverrideScope, AcadWarningSuppressScope, SysVarScope, IDisposable, CloneTransformResult

### Community 10 - "Community 10"
Cohesion: 0.22
Nodes (3): AutoBIMFusionExtension, IExtensionApplication, RibbonBuilder

### Community 11 - "Community 11"
Cohesion: 0.39
Nodes (1): DimensionUtils

### Community 12 - "Community 12"
Cohesion: 0.28
Nodes (2): DrawOrderPreserver, EntityTransformUtils

### Community 13 - "Community 13"
Cohesion: 0.25
Nodes (3): IComparer, FileUtil, WindowsNaturalComparer

### Community 14 - "Community 14"
Cohesion: 0.48
Nodes (1): DwgOptimizer

### Community 15 - "Community 15"
Cohesion: 0.52
Nodes (1): ViewportCollector

### Community 16 - "Community 16"
Cohesion: 0.4
Nodes (2): ICommand, ButtonCommandHandler

## Knowledge Gaps
- **1 isolated node(s):** `LineInfo`
  These have ≤1 connection - possible missing edges or undocumented components.
- **Thin community `Community 5`** (17 nodes): `DimensionStyleNormalizer.cs`, `DimensionStyleNormalizer`, `.BuildScaledStyleName()`, `.BuildStyleCache()`, `.CreateScaledStyle()`, `.FormatChange()`, `.FormatScale()`, `.FormatStyleVisualChanges()`, `.FormatValue()`, `.IsUsableMultiplier()`, `.NormalizeDimensions()`, `.NormalizeStyleVisualScale()`, `.PurgeUnusedReplacedStyles()`, `.ResolveMultiplier()`, `.ResolveVisualBakeMultiplier()`, `.ScaleVisualValue()`, `.TryResolveStyleRecord()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 6`** (16 nodes): `DimensionStyleDiagnosticUtils.cs`, `.ToString()`, `DimensionStyleDiagnosticUtils`, `.AddUsedDimensionStyleId()`, `.CollectUsedDimensionStyleIds()`, `.Escape()`, `.F()`, `.FormatColor()`, `.FormatDimensionStyle()`, `.FormatObjectId()`, `.FormatPropertyValue()`, `.FormatTextStyle()`, `.FormatValue()`, `.LogStyleSnapshot()`, `.ReadOptionalBool()`, `.ShouldLogDimensionStyle()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 7`** (11 nodes): `TextStyleCommands.cs`, `TextStyleCommands`, `.BuildSignature()`, `.ChooseMasterStyle()`, `.CollectStyles()`, `.DeleteStyles()`, `.MergeTextStyles()`, `.Normalize()`, `.ReassignBlockAttributes()`, `.ReassignStyles()`, `.ReassignStylesInBlock()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 11`** (9 nodes): `DimensionUtils.cs`, `DimensionUtils`, `.IsControlString()`, `.IsDimensionStyleOverrideMarker()`, `.IsRegApp()`, `.SkipOverridePayload()`, `.TryRemoveAcadDimensionStyleOverrideSection()`, `.TryRemoveDimensionStyleOverrides()`, `.TryRemoveDimensionStyleOverrideSection()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 12`** (9 nodes): `DrawOrderPreserver.cs`, `EntityTransformUtils.cs`, `DrawOrderPreserver`, `.Capture()`, `.Restore()`, `EntityTransformUtils`, `.EvaluateHatch()`, `.TransformEntity()`, `.DeepCloneAndTransform()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 14`** (7 nodes): `DwgOptimizer.cs`, `DwgOptimizer`, `.AddDictionaryIds()`, `.AddTableIds()`, `.ErasePurgedObjects()`, `.Optimize()`, `.PurgePass()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 15`** (7 nodes): `ViewportCollector.cs`, `ViewportCollector`, `.Collect()`, `.ComputeModelWindow()`, `.GetDcsToWcsMatrix()`, `.GetViewCenterWcs()`, `.ResolveScale()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 16`** (5 nodes): `ButtonCommandHandler.cs`, `ICommand`, `ButtonCommandHandler`, `.CanExecute()`, `.Execute()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `ViewportTransformer` connect `Community 0` to `Community 9`, `Community 12`?**
  _High betweenness centrality (0.044) - this node is a cross-community bridge._
- **Why does `LoggerFactory` connect `Community 8` to `Community 4`?**
  _High betweenness centrality (0.042) - this node is a cross-community bridge._
- **What connects `LineInfo` to the rest of the system?**
  _1 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.11 - nodes in this community are weakly interconnected._
- **Should `Community 1` be split into smaller, more focused modules?**
  _Cohesion score 0.09 - nodes in this community are weakly interconnected._