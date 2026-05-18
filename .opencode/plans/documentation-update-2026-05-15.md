# План обновления документации AutoBIMFusion

**Дата:** 2026-05-15  
**Статус:** Утверждён пользователем

---

## Файл 1: `README.md`

### Изменения:

1. **Секция "Процесс слияния"** — добавить 3 новых шага между шагами 4 и 5:
   - Шаг 5: `PhantomBlockCleaner` — удаление фантомных блоков (критичный порядок: ДО нормализации)
   - Шаг 6: `BlockBasePointEditor` — нормализация базовых точек блоков в левый нижний угол
   - Шаг 7: `BlockScaleApplier` — нормализация non-uniform scale блоков к 1.0
   - Обновить нумерацию существующих шагов 5→8, 6→9, 7→10

2. **Секция "Документация"** — добавить ссылку на `docs/LEGACY_UNIT_WORKAROUNDS.md`

---

## Файл 2: `docs/TECHNICAL_DOCUMENTATION.md`

### Изменения:

1. **Дата** → `2026-05-15`

2. **Секция "Ключевые классы"** — добавить новые строки в таблицу:
   - `PhantomBlockCleaner` | `AutoBIMFusion.Merge` | 3-pass detection и удаление фантомных блоков
   - `BlockBasePointEditor` | `AutoBIMFusion.Merge` | Нормализация базовых точек блоков (bottom-left corner)
   - `BlockScaleApplier` | `AutoBIMFusion.Merge` | Нормализация non-uniform block scale к 1.0
   - `StyleUnificationService` | `AutoBIMFusion.Merge` | Переименование text styles + GOST dimension styles
   - `DrawOrderPreserver` | `AutoBIMFusion.Merge` | Capture/restore SortentsTable draw order при клонировании
   - `EntityTransformUtils` | `AutoBIMFusion.Common` | Post-transform processing (hatch, dimensions, attributes)

3. **Секция "Merge pipeline"** — обновить диаграмму:
   ```
   CombineCommands
     -> FileUtil
     -> CombineOrchestrator
     -> ViewportLayoutExporter / LayoutProjectionProcessor
     -> PhantomBlockCleaner          ← NEW
     -> BlockBasePointEditor         ← NEW
     -> BlockScaleApplier            ← NEW
     -> BlockInserter
        -> StyleUnificationService   ← NEW (before WblockCloneObjects)
        -> WblockCloneObjects
     -> DimensionStyleNormalizer
     -> RasterImagePathFixer
     -> DrawingPurger.Optimize
     -> SaveAs(DwgVersion.AC1032)
   ```

4. **Новая секция "Extensions"** — описать 30 extension method файлов в `AutoBIMFusion.Common/Extensions/`:
   - `ObjectIdExtensions` — IsValidForOperation, EraseObject, GetDBObject
   - `EntityExtensions` — IsEntityOnLockedLayer, TransformBySafe
   - `DatabaseExtensions` — GetDocument, GetEditor
   - `ViewportsExtensions` — ResolveCustomScale, ComputeModelWindow, GetViewCenterWcs
   - `BlockReferenceExtensions` — GetBlockReferenceName
   - И другие для DBObject, Polylines, Curves, Hatchs и т.д.

5. **Новая секция "Drawing & Mist helpers"**:
   - `BlockReferences.cs` (694 строки) — comprehensive block utilities
   - `Generic.cs` (281 строка) — central utility class с tolerances, system variable management
   - Geometry utilities в `Mist/Geometry/`

---

## Файл 3: `docs/ALGORITHM.md`

### Изменения:

1. **Дата** → `2026-05-15`

2. **Секция 2 "Обработка одного DWG"** — добавить шаги после шага 7:
   - 2.5 `PhantomBlockCleaner.Clean()` — почему ДО нормализации (искажает extents calculations)
   - 2.6 `BlockBasePointEditor.NormalizeAllBlocksBasePoints()` — алгоритм offset compensation
   - 2.7 `BlockScaleApplier.NormalizeBlockScale()` — definition + reference inverse scaling

3. **Секция 5 "Вставка в целевой чертеж"** — добавить `StyleUnificationService` как шаг 1.5 (перед WblockCloneObjects)

4. **Уточнить** `ViewportTransformer.DeepCloneAndTransform` — draw order preservation через `DrawOrderPreserver`

---

## Файл 4: `docs/PROJECT_STRUCTURE.md`

### Изменения:

1. **Дата** → `2026-05-15`

2. **Развернуть структуру `AutoBIMFusion.Merge/Combine/`**:
   ```
   AutoBIMFusion.Merge/Combine/
     ├── BlockInserter.cs
     ├── CombineOrchestrator.cs
     ├── CombineResult.cs
     ├── CombineStatistics.cs
     ├── PhantomBlockCleaner.cs
     ├── BlockBasePointEditor.cs
     ├── BlockScaleApplier.cs
     ├── DrawingPurger.cs
     ├── RasterImagePathFixer.cs
     └── Layouts/
         ├── ViewportLayoutExporter.cs
         ├── LayoutProjectionProcessor.cs
         ├── ViewportCollector.cs
         ├── ViewportInfo.cs
         ├── ViewportTransformer.cs
         ├── ViewportScaleNormalizer.cs
         ├── ModelSpaceTrimmer.cs
         ├── DrawOrderPreserver.cs
         ├── DimensionStyleNormalizer.cs
         ├── StyleUnificationService.cs
         └── DimensionStyleDiagnosticUtils.cs
   ```

3. **Развернуть `AutoBIMFusion.Common/`**:
   ```
   AutoBIMFusion.Common/
     ├── AcadSupport/
     │   ├── AcadWarningSuppressScope.cs
     │   └── DatabaseUnitSyncScope.cs
     ├── Extensions/          (30 файлов extension methods)
     ├── Drawing/
     │   ├── BlockReferences.cs
     │   └── Entities.cs
     ├── Mist/
     │   ├── Generic.cs
     │   ├── AutoCAD/
     │   └── Geometry/
     ├── Helpers/
     │   ├── ExtentsUtils.cs
     │   ├── FileUtil.cs
     │   ├── EntityTransformUtils.cs
     │   └── LayoutUtil.cs
     ├── UiDialogService.cs
     ├── WindowsNaturalComparer.cs
     └── Logging/
         └── LoggerFactory.cs
   ```

4. **Обновить Public API boundaries** — добавить:
   - `PhantomBlockCleaner`
   - `BlockBasePointEditor`
   - `BlockScaleApplier`

---

## Файл 5: `docs/KNOWN_ISSUES.md`

### Изменения:

1. **Дата** → `2026-05-15`

2. **Добавить KI-6**: PhantomBlockCleaner false positives
   - **Где:** `PhantomBlockCleaner.FindPhantomBlocks`
   - **Риск:** Легитимные микро-блоки (≤15 entities, ≤15 units length) с большим смещением могут быть удалены
   - **Решение:** Добавить whitelist или configurable thresholds если потребуется

3. **Добавить KI-7**: BlockBasePointEditor пропускает dynamic/anonymous blocks
   - **Где:** `BlockBasePointEditor.ShouldSkipBlockDefinition`
   - **Риск:** Dynamic blocks остаются с оригинальными base points, могут вызвать misalignment
   - **Решение:** Добавить поддержку dynamic blocks если потребуется

4. **Секция "Исправлено"** — добавить строки:
   - Phantom block corruption extents → `PhantomBlockCleaner` (3-pass detection)
   - Block base point offset → `BlockBasePointEditor` (bottom-left normalization)
   - Non-uniform block scale distortion → `BlockScaleApplier` (definition + reference scaling)

---

## Файл 6: `docs/LEGACY_UNIT_WORKAROUNDS.md`

**Без изменений** — исторический архив, intentionally preserved.

---

## Итого

- **Обновлено файлов:** 5
- **Без изменений:** 1 (LEGACY_UNIT_WORKAROUNDS.md)
- **Новых секций:** ~8
- **Новых классов в документации:** 6
