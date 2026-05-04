# Graph Report - AutoBIMFusion  (2026-05-04)

## Corpus Check
- 42 files · ~17,735 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 257 nodes · 422 edges · 18 communities detected
- Extraction: 78% EXTRACTED · 22% INFERRED · 0% AMBIGUOUS · INFERRED: 91 edges (avg confidence: 0.8)
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
1. `DimensionStyleDiagnosticUtils` - 20 edges
2. `DimensionStyleNormalizer` - 13 edges
3. `ExtentsUtils` - 12 edges
4. `LayoutProjectionProcessor` - 12 edges
5. `SmartTextCommands` - 12 edges
6. `TransmittalCommands` - 11 edges
7. `TextStyleCommands` - 10 edges
8. `ViewportTransformer` - 9 edges
9. `DimensionUtils` - 8 edges
10. `CombineCommands` - 8 edges

## Surprising Connections (you probably didn't know these)
- None detected - all connections are within the same source files.

## Communities

### Community 0 - "Community 0"
Cohesion: 0.12
Nodes (5): DimensionScaleAccumulator, LayoutProjectionProcessor, PickMainViewport(), ViewportTransformer, LayoutUtil

### Community 1 - "Community 1"
Cohesion: 0.1
Nodes (8): BlockInserter, CombineOrchestrator, Fail(), Ok(), Warn(), ExtentsUtils, ModelSpaceTrimmer, StringUtils

### Community 2 - "Community 2"
Cohesion: 0.14
Nodes (2): SmartTextCommands, DimensionUtils

### Community 3 - "Community 3"
Cohesion: 0.21
Nodes (1): DimensionStyleDiagnosticUtils

### Community 4 - "Community 4"
Cohesion: 0.17
Nodes (3): CombineStatistics, CombineCommands, UiDialogService

### Community 5 - "Community 5"
Cohesion: 0.13
Nodes (5): AutoBIMFusionExtension, RasterImagePathFixer, IExtensionApplication, RibbonBuilder, RibbonIconLoader

### Community 6 - "Community 6"
Cohesion: 0.17
Nodes (6): AcadUnitScalingOverrideScope, AcadWarningSuppressScope, ManagedSystemVariable, IDisposable, ViewportLayoutExporter, CloneTransformResult

### Community 7 - "Community 7"
Cohesion: 0.3
Nodes (1): DimensionStyleNormalizer

### Community 8 - "Community 8"
Cohesion: 0.29
Nodes (1): TransmittalCommands

### Community 9 - "Community 9"
Cohesion: 0.33
Nodes (1): TextStyleCommands

### Community 10 - "Community 10"
Cohesion: 0.28
Nodes (2): DrawOrderPreserver, EntityTransformUtils

### Community 11 - "Community 11"
Cohesion: 0.25
Nodes (3): IComparer, FileUtil, WindowsNaturalComparer

### Community 12 - "Community 12"
Cohesion: 0.36
Nodes (3): ILogEventSink, DiagnosticSink, LoggerFactory

### Community 13 - "Community 13"
Cohesion: 0.52
Nodes (1): ViewportCollector

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
- **Thin community `Community 2`** (24 nodes): `DimensionUtils.cs`, `SmartTextCommands.cs`, `SmartTextCommands`, `.AreHeightsClose()`, `.AreTextsClose()`, `.CollectTextElements()`, `.CombineGroupText()`, `.EscapeMTextContent()`, `.EstimateTextWidth()`, `.GetTextBoundsAlongAxis()`, `.LowerBoundByPerp()`, `.ProjectPerpendicular()`, `.SmartGroupText()`, `.SmartMergeModelText()`, `.ClearDimensionOverrides()`, `DimensionUtils`, `.IsControlString()`, `.IsDimensionStyleOverrideMarker()`, `.IsRegApp()`, `.SkipOverridePayload()`, `.TryRemoveAcadDimensionStyleOverrideSection()`, `.TryRemoveDimensionStyleOverrides()`, `.TryRemoveDimensionStyleOverrideSection()`, `.GetSharedLogger()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 3`** (20 nodes): `DimensionStyleDiagnosticUtils.cs`, `DimensionStyleDiagnosticUtils`, `.CollectDimensionStyleNames()`, `.CollectTextStyleNames()`, `.CreateSnapshot()`, `.Escape()`, `.FormatColor()`, `.FormatDimensionStyle()`, `.FormatDouble()`, `.FormatSnapshot()`, `.FormatTextStyle()`, `.IsControlString()`, `.IsDimensionStyleOverrideMarker()`, `.IsRegApp()`, `.IsUserStyle()`, `.LogNewStylesBeforeMerge()`, `.LogStyleSnapshot()`, `.SkipOverridePayload()`, `.TryRemoveAcadDimensionStyleOverrideSection()`, `.TryRemoveDimensionStyleOverrideSection()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 7`** (14 nodes): `DimensionStyleNormalizer.cs`, `DimensionStyleNormalizer`, `.BuildScaledStyleName()`, `.BuildStyleCache()`, `.CreateScaledStyle()`, `.FormatScale()`, `.IsUsableMultiplier()`, `.NormalizeModelSpaceDimensions()`, `.NormalizeStyleVisualScale()`, `.PurgeUnusedReplacedStyles()`, `.ResolveMultiplier()`, `.ResolveVisualBakeMultiplier()`, `.ScaleVisualValue()`, `.TryResolveStyleRecord()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 8`** (12 nodes): `TransmittalCommands.cs`, `TransmittalCommands`, `.ConfigureTransmittalInfo()`, `.ConvertMemberValue()`, `.CreateETransmitZip()`, `.PrepareOutputFolders()`, `.SafeGetTypes()`, `.SetMemberValue()`, `.TryCreateTransmittalOperation()`, `.TryDeleteTempFolder()`, `.TryLoadAssemblyByName()`, `.TryLoadAssemblyByPath()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 9`** (11 nodes): `TextStyleCommands.cs`, `TextStyleCommands`, `.BuildSignature()`, `.ChooseMasterStyle()`, `.CollectStyles()`, `.DeleteStyles()`, `.MergeTextStyles()`, `.Normalize()`, `.ReassignBlockAttributes()`, `.ReassignStyles()`, `.ReassignStylesInBlock()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 10`** (9 nodes): `DrawOrderPreserver.cs`, `EntityTransformUtils.cs`, `DrawOrderPreserver`, `.Capture()`, `.Restore()`, `EntityTransformUtils`, `.EvaluateHatch()`, `.TransformEntity()`, `.DeepCloneAndTransform()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 13`** (7 nodes): `ViewportCollector.cs`, `ViewportCollector`, `.Collect()`, `.ComputeModelWindow()`, `.GetDcsToWcsMatrix()`, `.GetViewCenterWcs()`, `.ResolveScale()`
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

- **Why does `DimensionStyleDiagnosticUtils` connect `Community 3` to `Community 2`?**
  _High betweenness centrality (0.085) - this node is a cross-community bridge._
- **Why does `ViewportTransformer` connect `Community 0` to `Community 1`, `Community 10`, `Community 6`?**
  _High betweenness centrality (0.049) - this node is a cross-community bridge._
- **What connects `LineInfo` to the rest of the system?**
  _1 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.12 - nodes in this community are weakly interconnected._
- **Should `Community 1` be split into smaller, more focused modules?**
  _Cohesion score 0.1 - nodes in this community are weakly interconnected._
- **Should `Community 2` be split into smaller, more focused modules?**
  _Cohesion score 0.14 - nodes in this community are weakly interconnected._
- **Should `Community 5` be split into smaller, more focused modules?**
  _Cohesion score 0.13 - nodes in this community are weakly interconnected._