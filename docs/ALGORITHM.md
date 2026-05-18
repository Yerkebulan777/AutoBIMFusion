# Алгоритм работы AutoBIMFusion

**Последнее обновление:** 2026-05-15

## 1. Запуск и выбор файлов

1. Команда `MERGEDWG` входит в `AutoBIMFusion.Plugin.Commands.CombineCommands.MergeDwgFolderCommand`.
2. `SemaphoreSlim.WaitAsync(0)` блокирует повторный запуск параллельной merge-операции (неблокирующая проверка).
3. Пользователь выбирает папку через `UiDialogService.TrySelectFolder`.
4. `FileUtil.GetFiles` собирает `.dwg` до 3 уровней вложенности, пропуская имена с префиксом `#` и файлы больше 15 МБ.
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
8. `PhantomBlockCleaner.Clean()` — обнаружение фантомных блоков по BoundingBox block definition: диагональ габаритов должна быть ≤15 единиц, а максимальное расстояние углов BoundingBox от начала координат должно превышать adaptive offset threshold. Критично выполнять ДО нормализации базовых точек, т.к. phantom geometry искажает extents calculations.
9. `BlockBasePointEditor.NormalizeAllBlocksBasePoints()` — вычисление extents блока (игнорируя entities с diagonal < 25), расчёт offset от origin до bottom-left corner, сдвиг definition geometry на `-offset` и всех block references на `+offset` (компенсированный через rotation/scale matrix). Пропускает layouts, anonymous, dynamic, xref блоки.
10. `BlockScaleApplier.NormalizeBlockScale()` — масштабирование всех entities в block definition на refScale, обратное масштабирование всех block reference ScaleFactors, обновление anonymous blocks для dynamic blocks, сохранение пропорций при разных масштабах вставок.

## 3. Проекция Layout в Model Space

*Реализовано в `LayoutProjectionProcessor.ProjectLayoutToModelSpace`.*

### Без видовых экранов

1. Paper Space entities выбранного Layout клонируются в Model Space.
2. Применяется масштаб по умолчанию `1:100` (`MaxScaleMultiplier = 100.0`): строится матрица `Scaling(100) * Displacement(-minPt)`.
3. Исходное содержимое Paper Space очищается.

### С видовыми экранами

1. Главный vpt выбирается через `ViewportInfo.PickMainViewport`.
2. Рабочий масштаб main vpt нормализуется до `1:100` для всех масштабов; `geometryScale = originalScale / (1/100)`, а `Dimlfac = 1 / geometryScale` сохраняет числовые значения размеров.
3. `ViewportTransformer.CollectModelEntitiesWithExtents` снимает снимок объектов Model Space.
4. Для каждого aux viewport: строится матрица `BuildMatrix(main, aux)`, отбираются объекты внутри его модельного окна, выполняется `DeepCloneAndTransform` (с capture/restore draw order через `DrawOrderPreserver`), удаляются исходные объекты за пределами main window (`EraseEntitiesOutsideMainWindow`).
5. Если `geometryScale != 1`: `ScaleModelSpaceObjects` масштабирует Model Space вокруг `ViewCenter` main vpt к рабочему масштабу `1:100`.
6. Paper Space переносится в Model Space через матрицу `BuildPaperToMainMatrix(mainNormalized)`.

## 4. Тримминг и размеры

*Выполняется внутри `ViewportLayoutExporter.PrepareDatabaseForMerge`.*

1. Если рассчитана рамка листа (`projection.FrameBounds.HasValue`), `ModelSpaceTrimmer.TrimOutside` удаляет объекты вне рамки.
2. `DimensionStyleDiagnosticUtils.LogStyleSnapshot` пишет снимок `source-after-normalize-before-clone` только при `LOG_LEVEL=DEBUG`.
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
8. `DimensionStyleDiagnosticUtils.LogStyleSnapshot` пишет снимок `target-after-clone` только при `LOG_LEVEL=DEBUG`.

## 6. Финализация

1. `RasterImagePathFixer.CopyImagesToTargetFolder` копирует растры рядом с итоговым DWG и обновляет пути.
2. `DimensionStyleDiagnosticUtils.LogStyleSnapshot` пишет снимок `target-after-merge` только при `LOG_LEVEL=DEBUG`.
3. `DrawingPurger.Optimize` выполняет до 10 проходов `Purge`.
4. Итоговый файл сохраняется через `SaveAs(savePath, DwgVersion.AC1032)`.
5. AutoCAD получает команды `REGENALL` и `ZOOM EXTENTS`.

## 7. Диагностический headless-сценарий

`tools/Run-MergeDwgDiagTest.ps1` строит core bundle с `/p:CoreConsoleDiagnostics=true`, создает script file для `accoreconsole.exe` и вызывает `MERGEDWG_DIAG_TEST`.

В текущих C# исходниках нет `[CommandMethod("MERGEDWG_DIAG_TEST")]`, поэтому сценарий не является рабочим acceptance test до восстановления команды или изменения скрипта.
