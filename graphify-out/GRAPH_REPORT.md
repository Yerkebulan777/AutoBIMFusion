# Graph Report - AutoBIMFusion  (2026-05-02)

## Corpus Check
- 56 files · ~20,846 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 280 nodes · 515 edges · 19 communities detected
- Extraction: 67% EXTRACTED · 33% INFERRED · 0% AMBIGUOUS · INFERRED: 168 edges (avg confidence: 0.8)
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
- [[_COMMUNITY_Community 18|Community 18]]

## God Nodes (most connected - your core abstractions)
1. `DimensionStyleDiagnosticUtils` - 21 edges
2. `DimensionHealer` - 14 edges
3. `SmartTextCommands` - 12 edges
4. `ExtentsUtils` - 12 edges
5. `TransmittalCommands` - 11 edges
6. `TextStyleCommands` - 10 edges
7. `ViewportTransformer` - 10 edges
8. `MergeCommands` - 9 edges
9. `LayoutProjectionProcessor` - 9 edges
10. `AILog` - 7 edges

## Surprising Connections (you probably didn't know these)
- None detected - all connections are within the same source files.

## Communities

### Community 0 - "Community 0"
Cohesion: 0.13
Nodes (5): DrawOrderPreserver, LayoutProjectionProcessor, ModelSpaceTrimmer, ViewportTransformer, PickMainViewport()

### Community 1 - "Community 1"
Cohesion: 0.19
Nodes (1): DimensionStyleDiagnosticUtils

### Community 2 - "Community 2"
Cohesion: 0.17
Nodes (4): JoinCommands, LineInfo, MergeCommands, UiDialogService

### Community 3 - "Community 3"
Cohesion: 0.13
Nodes (5): AutoBIMFusionExtension, IExtensionApplication, RasterImagePathFixer, RibbonBuilder, RibbonIconLoader

### Community 4 - "Community 4"
Cohesion: 0.21
Nodes (2): TransmittalCommands, AILog

### Community 5 - "Community 5"
Cohesion: 0.16
Nodes (3): StyleExportCommands, MergeStatistics, StyleExportUtils

### Community 6 - "Community 6"
Cohesion: 0.16
Nodes (6): MergeCoordinator, Fail(), Ok(), Warn(), StringUtils, FileHelper

### Community 7 - "Community 7"
Cohesion: 0.29
Nodes (1): DimensionHealer

### Community 8 - "Community 8"
Cohesion: 0.21
Nodes (3): ViewportCollector, ViewportLayoutExporter, LayoutUtil

### Community 9 - "Community 9"
Cohesion: 0.28
Nodes (1): SmartTextCommands

### Community 10 - "Community 10"
Cohesion: 0.22
Nodes (2): ExtentsUtils, BlockInserter

### Community 11 - "Community 11"
Cohesion: 0.33
Nodes (1): TextStyleCommands

### Community 12 - "Community 12"
Cohesion: 0.31
Nodes (3): ILogEventSink, DiagnosticSink, LoggerFactory

### Community 13 - "Community 13"
Cohesion: 0.39
Nodes (4): AcadUnitScalingOverrideScope, AcadWarningSuppressScope, ManagedSystemVariable, IDisposable

### Community 14 - "Community 14"
Cohesion: 0.29
Nodes (3): IComparer, FileEnumerator, WindowsNaturalComparer

### Community 15 - "Community 15"
Cohesion: 0.43
Nodes (1): EntityTransformUtils

### Community 16 - "Community 16"
Cohesion: 0.53
Nodes (1): DwgOptimizer

### Community 17 - "Community 17"
Cohesion: 0.4
Nodes (2): ICommand, ButtonCommandHandler

### Community 18 - "Community 18"
Cohesion: 0.67
Nodes (1): FolderSelector

## Knowledge Gaps
- **1 isolated node(s):** `LineInfo`
  These have ≤1 connection - possible missing edges or undocumented components.
- **Thin community `Community 1`** (22 nodes): `DimensionStyleDiagnosticUtils.cs`, `DimensionStyleDiagnosticUtils`, `.ClearDimensionOverrides()`, `.CollectDimensionStyleNames()`, `.CollectTextStyleNames()`, `.CreateSnapshot()`, `.Escape()`, `.FormatColor()`, `.FormatDimensionStyle()`, `.FormatDouble()`, `.FormatSnapshot()`, `.FormatTextStyle()`, `.IsControlString()`, `.IsDimensionStyleOverrideMarker()`, `.IsRegApp()`, `.IsUserStyle()`, `.LogNewStylesBeforeMerge()`, `.LogStyleSnapshot()`, `.SkipOverridePayload()`, `.TryRemoveAcadDimensionStyleOverrideSection()`, `.TryRemoveDimensionStyleOverrides()`, `.TryRemoveDimensionStyleOverrideSection()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 4`** (17 nodes): `TransmittalCommands.cs`, `AILog.cs`, `TransmittalCommands`, `.ConfigureTransmittalInfo()`, `.ConvertMemberValue()`, `.CreateETransmitZip()`, `.PrepareOutputFolders()`, `.SafeGetTypes()`, `.SetMemberValue()`, `.TryCreateTransmittalOperation()`, `.TryDeleteTempFolder()`, `.TryLoadAssemblyByName()`, `.TryLoadAssemblyByPath()`, `AILog`, `.Log()`, `.TryWriteToEditor()`, `.Warn()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 7`** (15 nodes): `DimensionHealer.cs`, `DimensionHealer`, `.AreClose()`, `.Heal()`, `.HealDimensionStyles()`, `.IsImperialOverride()`, `.IsInfected()`, `.NormalizeDimensionVisualScale()`, `.NormalizeStyleVisualScale()`, `.RoundDimensionTextValues()`, `.RoundStyleTextValues()`, `.RoundTextValue()`, `.ScaleVisualValue()`, `.TryGetStyleDimscale()`, `.TryGetStyleSnapshot()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 9`** (13 nodes): `SmartTextCommands.cs`, `SmartTextCommands`, `.AreHeightsClose()`, `.AreTextsClose()`, `.CollectTextElements()`, `.CombineGroupText()`, `.EscapeMTextContent()`, `.EstimateTextWidth()`, `.GetTextBoundsAlongAxis()`, `.LowerBoundByPerp()`, `.ProjectPerpendicular()`, `.SmartGroupText()`, `.SmartMergeModelText()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 10`** (13 nodes): `BlockInserter.cs`, `ExtentsUtils.cs`, `ExtentsUtils`, `.IsEntityPointIn()`, `.IsPointIn()`, `.SyncUnits()`, `.Transform()`, `.TryGetScaleRatio()`, `.Union()`, `BlockInserter`, `.CalcInsertionPoint()`, `.InsertNativeObjects()`, `.SyncUnits()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 11`** (11 nodes): `TextStyleCommands.cs`, `TextStyleCommands`, `.BuildSignature()`, `.ChooseMasterStyle()`, `.CollectStyles()`, `.DeleteStyles()`, `.MergeTextStyles()`, `.Normalize()`, `.ReassignBlockAttributes()`, `.ReassignStyles()`, `.ReassignStylesInBlock()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 15`** (7 nodes): `EntityTransformUtils.cs`, `EntityTransformUtils`, `.AdjustMLeaderScale()`, `.EvaluateHatch()`, `.GetScaleFactor()`, `.RemoveDimStyleOverrides()`, `.TransformEntity()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 16`** (6 nodes): `DwgOptimizer.cs`, `DwgOptimizer`, `.AddDictionaryIds()`, `.AddTableIds()`, `.Optimize()`, `.PurgePass()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 17`** (5 nodes): `ButtonCommandHandler.cs`, `ICommand`, `ButtonCommandHandler`, `.CanExecute()`, `.Execute()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 18`** (3 nodes): `FolderSelector.cs`, `FolderSelector`, `.TrySelectFolder()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **What connects `LineInfo` to the rest of the system?**
  _1 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.13 - nodes in this community are weakly interconnected._
- **Should `Community 3` be split into smaller, more focused modules?**
  _Cohesion score 0.13 - nodes in this community are weakly interconnected._