# Алгоритм работы AutoBIMFusion

**Последнее обновление:** 2026-05-24

## 1. Запуск и выбор файлов

1. Команда `MERGEDWG` входит в `AutoBIMFusion.Plugin.Commands.CombineCommands.MergeDwgFolderCommand`.
2. `SemaphoreSlim.WaitAsync(0)` блокирует повторный запуск параллельной merge-операции (неблокирующая проверка).
3. Пользователь выбирает папку через `UiDialogService.TrySelectFolder`.
4. `FileUtil.GetFiles` собирает `.dwg` (только текущая папка, без рекурсии), пропуская имена с префиксом `#` и файлы больше 15 МБ.
5. Файлы сортируются естественным порядком через `WindowsNaturalComparer` по относительному пути от корневой папки.

## 2. Обработка одного DWG

*Реализовано в `AutoBIMFusion.Merge.CombineOrchestrator.MergeSingleFile`.*

1. `FileUtil.TryValidateDwg` проверяет наличие, ненулевой размер и возможность открыть файл как DWG.
2. `ViewportLayoutExporter.PrepareDatabaseForMerge` открывает файл в фоновой базе `new Database(false, true)`.
3. После `ReadDwgFile` вызывается `CloseInput(true)`.
4. `ExtentsUtils.SyncUnits` приводит базу к метрическим единицам (мм/метрика). Единицы всех не-xref `BlockTableRecord` также приводятся к `Millimeters`.
5. Layout выбирается через `LayoutUtil.TryFindFirstLayout`: минимальный `TabOrder` среди Paper Space layouts (записи с `ModelType = true` пропускаются).
6. `ViewportCollector.Collect` собирает viewports выбранного листа.
7. `ExtentsUtils.GetDatabaseExtents` вычисляет границы подготовленной базы в `CombineOrchestrator` перед вставкой.
8. `OutOfFrameEntityCleaner.Clean()` — удаление малых объектов после проекции Layout в Model Space: диагональ bbox должна быть ≤15 единиц, а центральная точка bbox по XY должна находиться за рамкой листа. Рамка определяется как самый габаритный `BlockReference` из LayoutSpace и трансформируется той же матрицей, что и Paper Space. Критично выполнять ДО нормализации базовых точек, т.к. мелкая мусорная геометрия может исказить extents calculations.
9. `BlockBasePointEditor.NormalizeAllBlocksBasePoints()` — вычисление extents блока (игнорируя entities с diagonal < 25), расчёт offset от origin до bottom-left corner, сдвиг definition geometry на `-offset` и всех block references на `+offset` (компенсированный через rotation/scale matrix). Пропускает layouts, anonymous, dynamic, xref блоки.
10. `BlockScaleApplier.NormalizeBlockScale()` — масштабирование всех entities в block definition на refScale, обратное масштабирование всех block reference ScaleFactors, обновление anonymous blocks для dynamic blocks, сохранение пропорций при разных масштабах вставок.

## 3. Проекция Layout в Model Space

*Реализовано в `LayoutProjectionProcessor.ProjectLayoutToModelSpace`.*

### Без видовых экранов

1. Paper Space entities выбранного Layout клонируются в Model Space.
2. Применяется масштаб по умолчанию `1:100` (`MaxScaleMultiplier = 100.0`): строится матрица `Scaling(100) * Displacement(-minPt)`.
3. Исходное содержимое Paper Space очищается.

### С видовыми экранами

1. Главный vpt выбирается через `ViewportInfo.PickMainViewport` (см. **Алгоритм выбора главного VP** ниже).
2. Рабочий масштаб main vpt нормализуется до `1:100` через `ViewportScaleNormalizer.Normalize` (см. **Алгоритм нормализации масштаба** ниже).
3. `ViewportTransformer.CollectModelEntitiesWithExtents` снимает снимок объектов Model Space.
4. Для каждого aux viewport: строится матрица `BuildMatrix(main, aux)`, отбираются объекты внутри его модельного окна, выполняется `DeepCloneAndTransform` (с capture/restore draw order через `DrawOrderPreserver`), удаляются исходные объекты за пределами main window (`EraseEntitiesOutsideMainWindow`).
5. Если `geometryScale != 1`: `ScaleModelSpaceObjects` масштабирует Model Space вокруг `ViewCenter` main vpt к рабочему масштабу `1:100`. Клоны aux-VP пропускаются (`clonedIdsToSkip`) — они уже учли масштаб через матрицу в шаге 4.
6. Paper Space переносится в Model Space через матрицу `BuildPaperToMainMatrix(mainNormalized)`.

---

### Выбор объектов aux viewport

`ViewportTransformer.SelectModelInside` использует пересечение AABB объекта с модельным окном aux viewport как основной критерий принадлежности.

Критичное правило: крупный объект, который пересекает окно aux viewport только краем и выходит за грань листа, должен оставаться выбранным. Нельзя отбрасывать его только потому, что центр его bbox находится вне окна. Такой объект должен попасть в `DeepCloneAndTransform`, где к нему применяется актуальная матрица `scaleMatrix * auxToMain`.

Если отфильтровать такие объекты по центру bbox, они остаются в исходных координатах Model Space и после вставки дают регрессию: смещаются только крупногабаритные блоки/объекты, выходящие за рамку листа, тогда как обычные объекты выглядят корректно.

Сохраняемые фильтры:

- `OutsideWindow`: AABB не пересекает окно aux viewport.
- `SmallPartialOutsideWindow`: малый объект частично пересекает окно, но не помещается целиком.
- `HugeInMainWindow`: большой объект главного VP не должен стать контентом aux VP.

Запрещенный фильтр: `CenterOutsideWindow` для крупных частично пересекающихся объектов.

---

### Алгоритм выбора главного VP (`ViewportInfo.PickMainViewport`)

Главный VP определяет масштаб нормализации и `Dimlfac` для всего листа.

**Шаг 1 — модальный масштаб.**
VP группируются по `CustomScale` (округление до 4 знаков, чтобы исключить плавающую точку). Выбирается группа с наибольшим числом VP. Тай-брейк по суммарной `PaperArea` группы.

**Шаг 2 — максимальная площадь внутри группы.**
Среди VP модального масштаба выбирается тот, у которого `PaperArea = Width × Height` максимальна.

**Пример:** лист с VP 1:25 × 3 шт. + VP 1:100 × 1 шт.
→ модальный масштаб = 0.04 → выбирается самый большой 1:25-VP.

> **Почему не `PaperArea / CustomScale` (до 2026-05-21):**
> Деление на малый `CustomScale` (0.01 для 1:100) давало обзорному VP score в 4× выше,
> чем рабочим VP при 1:25. Обзорный VP выигрывал как «главный», что приводило к
> `geometryScale = 1` и `Dimlfac = 1.0`. Контент 1:25-VP масштабировался ×4 через
> `auxToMain`, но `Dimlfac` это не компенсировал → размеры показывали значения
> в 4 раза больше реальных.

---

### Алгоритм нормализации масштаба (`ViewportScaleNormalizer.Normalize`)

Цель: привести геометрию и размеры к рабочему масштабу `1:100` (`workingCustomScale = 0.01`).

| Величина | Формула | Пример (customScale = 0.04) |
|---|---|---|
| `workingCustomScale` | `1 / 100` = константа | `0.01` |
| `geometryScale` | `customScale / workingCustomScale` | `0.04 / 0.01 = 4` |
| `Dimlfac` (linearScaleMultiplier) | `1 / geometryScale` | `1 / 4 = 0.25` |
| `Dimscale` (targetVisualScale) | `100` = константа | `100` |

**Смысл каждой величины:**

- `geometryScale` — во сколько раз растягивается Model Space, чтобы контент выглядел так же через viewport 1:100, как через оригинальный viewport 1:25. Применяется в `ScaleModelSpaceObjects`.
- `Dimlfac` — компенсирует растяжение геометрии для числового значения размеров. Если линия 1000 мм стала 4000 мм (×4), `Dimlfac = 0.25` отображает `4000 × 0.25 = 1000` — исходное значение.
- `Dimscale` — масштабирует визуальные атрибуты (стрелки, высоту текста) под рабочий масштаб 1:100. Не влияет на отображаемое число.

**Полная цепочка для aux-VP:**

Контент вспомогательного VP масштабируется дважды:
1. `auxToMain` net scale = `auxScale / mainScale` (из `BuildMatrix`)
2. Дополнительный `geometryScale` через `scaleMatrix` в `ProjectAuxViewport`

Итоговый масштаб aux-контента = `geometryScale × (auxScale / mainScale)` = `auxScale / workingCustomScale`.

Корректный `Dimlfac` для такого контента = `workingCustomScale / auxScale`.

При aux ≠ main масштабе применяется один глобальный `Dimlfac` от главного VP. Aux-VP другого масштаба получают неточный `Dimlfac` — **known limitation** (требует per-VP tracking).

## 4. Тримминг и размеры

*Выполняется внутри `ViewportLayoutExporter.PrepareDatabaseForMerge`.*

1. Если рассчитана рамка листа (`projection.FrameBounds.HasValue`), `OutOfFrameEntityCleaner.Clean` удаляет только малые объекты, центр bbox которых находится за рамкой.
2. `DimensionStyleDiagnosticUtils.LogStyleSnapshot` пишет снимок `source-after-normalize-before-clone` в Debug-сборках или при `LOG_LEVEL=DEBUG`.
3. После вставки в target `DimensionStyleNormalizer` удаляет DSTYLE overrides из XData/ExtensionDictionary и пересчитывает скопированные размеры.

## 5. Вставка в целевой чертеж

*Реализовано в `BlockInserter.InsertNativeObjects`.*

1. `CombineOrchestrator` берёт `DocumentLock` на целевой документ.
2. `ExtentsUtils.SyncUnits` приводит target к мм/метрика.
2.5 `StyleUnificationService.NormalizeTextStyleNames()` — переименование text styles по схеме `{font}-{height}-{modifiers}` для предотвращения коллизий имён при WblockCloneObjects.
2.6 `StyleUnificationService.ApplyGostToAllStyles()` — применение GOST параметров (ISOCPEUR, 2.5mm text, 1.25mm arrows) ко всем dimension styles в source database.
3. `WblockCloneObjects` оборачивается в `DatabaseUnitSyncScope`: единицы и `Dimalt` source временно выравниваются с target, чтобы AutoCAD не применял скрытое масштабирование (метрика ↔ имперская).
4. Model Space entities подготовленной source DB клонируются через `WblockCloneObjects` с `DuplicateRecordCloning.Ignore`.
5. К каждому клонированному объекту применяется displacement.
6. Скопированные размеры приводятся к чистому GOST-стилю, DSTYLE overrides удаляются, dimension blocks пересчитываются.
7. Следующий лист размещается справа от предыдущего; зазор равен `Max(1.0, Round(Max(width, height) * gapPercent, 0))`.
8. `DimensionStyleDiagnosticUtils.LogStyleSnapshot` пишет снимок `target-after-clone` в Debug-сборках или при `LOG_LEVEL=DEBUG`.

`insertX`/`insertY` рассчитываются из `placementBounds`, которые вычисляются через `ExtentsUtils.ComputeLiveBounds(sourceDb, sourceIds)` после подготовки source DB, нормализации базовых точек блоков и нормализации масштаба block references. Это гарантирует, что вектор сдвига строится по актуальной геометрии, а не по устаревшему `Database.Extmin/Extmax` или cached extents. После clone+displacement `worldBounds` пересчитывается по уже вставленным объектам и используется как `_rightMax` для следующего листа.

## 6. Финализация

1. `RasterImagePathFixer.CopyImagesToTargetFolder` копирует растры рядом с итоговым DWG и обновляет пути.
2. `DimensionStyleDiagnosticUtils.LogStyleSnapshot` пишет снимок `target-after-merge` в Debug-сборках или при `LOG_LEVEL=DEBUG`.
3. `DrawingPurger.Optimize` выполняет до 10 проходов `Purge`.
4. Итоговый файл сохраняется через `SaveAs(savePath, DwgVersion.AC1032)`.
5. AutoCAD получает команды `REGENALL` и `ZOOM EXTENTS`.

## 7. Диагностический headless-сценарий

`tools/Run-MergeDwgDiagTest.ps1` строит core bundle с `/p:CoreConsoleDiagnostics=true`, создает script file для `accoreconsole.exe` и вызывает `MERGEDWG_DIAG_TEST`.

В текущих C# исходниках нет `[CommandMethod("MERGEDWG_DIAG_TEST")]`, поэтому сценарий не является рабочим acceptance test до восстановления команды или изменения скрипта.
