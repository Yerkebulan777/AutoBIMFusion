# Алгоритм работы AutoBIMFusion

**Последнее обновление:** 2026-05-07

## 1. Запуск и выбор файлов

1. Команда `MERGEDWG` входит в `CombineCommands.MergeDwgFolderCommand`.
2. `SemaphoreSlim.WaitAsync(0)` блокирует повторный запуск параллельной merge-операции (неблокирующая проверка).
3. Пользователь выбирает папку через `UiDialogService.TrySelectFolder`.
4. `FileUtil.GetFiles` собирает `.dwg` до 3 уровней вложенности, пропуская имена с префиксом `#` и файлы больше 15 МБ.
5. Файлы сортируются естественным порядком через `WindowsNaturalComparer` по относительному пути от корневой папки.

## 2. Обработка одного DWG

*Реализовано в `CombineOrchestrator.MergeSingleFile`.*

1. `FileUtil.TryValidateDwg` проверяет наличие, ненулевой размер и возможность открыть файл как DWG.
2. `ViewportLayoutExporter.PrepareDatabaseForMerge` открывает файл в фоновой базе `new Database(false, true)`.
3. После `ReadDwgFile` вызывается `CloseInput(true)`.
4. `ExtentsUtils.SyncUnits` приводит базу к метрическим единицам (мм/метрика). Единицы всех не-xref `BlockTableRecord` также приводятся к `Millimeters`.
5. Layout выбирается через `LayoutUtil.TryFindFirstLayout`: минимальный `TabOrder` среди Paper Space layouts (записи с `ModelType = true` пропускаются).
6. `ViewportCollector.Collect` собирает viewports выбранного листа.
7. `ExtentsUtils.GetDatabaseExtents` вычисляет границы подготовленной базы в `CombineOrchestrator` перед вставкой.

## 3. Проекция Layout в Model Space

*Реализовано в `LayoutProjectionProcessor.ProjectLayoutToModelSpace`.*

### Без видовых экранов

1. Paper Space entities выбранного Layout клонируются в Model Space.
2. `ViewportTransformer.UnlockTextStylesHeight` разблокирует высоты текстовых стилей перед масштабированием.
3. Применяется масштаб по умолчанию `1:100` (`MaxScaleMultiplier = 100.0`): строится матрица `Scaling(100) * Displacement(-minPt)`.
4. `ViewportTransformer.FinalizeModelSpaceDimensionLinearScales` финализирует линейные масштабы размеров.
5. Исходное содержимое Paper Space очищается.

### С видовыми экранами

1. Главный vpt выбирается через `ViewportInfo.PickMainViewport`.
2. Рабочий масштаб main vpt зажимается (`ClampMainViewportScale`) до `1:100` для более мелких масштабов (когда `1/scale < 100`, т.е. 1:50, 1:20 и др.); `clampRatio = originalScale / clampedScale`.
3. `ViewportTransformer.CollectModelEntitiesWithExtents` снимает снимок объектов Model Space; `NormalizeDimensionsInsideViewport` обрабатывает размеры main vpt с учётом `clampRatio`.
4. Для каждого aux viewport: строится матрица `BuildMatrix(main, aux)`, отбираются объекты внутри его модельного окна, нормализуются размеры aux vpt, выполняется `DeepCloneAndTransform`, удаляются исходные объекты за пределами main window (`EraseEntitiesOutsideMainWindow`), повторно нормализуются размеры main vpt.
5. Если `clampRatio > 1`: `ScaleModelSpaceObjects` масштабирует Model Space вокруг `ViewCenter` main vpt на `clampRatio` (после `UnlockTextStylesHeight`).
6. Paper Space переносится в Model Space через матрицу `BuildPaperToMainMatrix(mainClamped)`.
7. `FinalizeModelSpaceDimensionLinearScales` финализирует линейные масштабы размеров.

## 4. Тримминг и размеры

*Выполняется внутри `ViewportLayoutExporter.PrepareDatabaseForMerge`.*

1. Если рассчитана рамка листа (`projection.FrameBounds.HasValue`), `ModelSpaceTrimmer.TrimOutside` удаляет объекты вне рамки.
2. `DimensionStyleDiagnosticUtils.LogStyleSnapshot` пишет снимок `source-after-normalize-before-clone`.
3. `DimensionStyleNormalizer.NormalizeDimensionStyleForViewport` клонирует текущий стиль размера под именем `{baseName}_{Scale}`, если такой ещё не создан; при clamp VP суффикс использует итоговый рабочий множитель (`ResolveFinalViewportMultiplier`).
4. Визуальные параметры (`Dimtxt`, `Dimasz`, `Dimtsz`, `Dimexo`, `Dimexe`, `Dimgap`, `Dimdli`, `Dimdle`, `Dimcen`, `Dimtvp`, `Dimfxlen`) умножаются через `ResolveVisualMultiplier` — использует исходный `Dimscale` если он корректен, иначе нормализованный масштаб vpt. `Dimscale = 1.0` в итоговом стиле.
5. DSTYLE overrides не возникают: `DatabaseUnitSyncScope` устраняет первопричину (см. `Application/AcadSupport/DatabaseUnitSyncScope.cs`).
6. `FinalizeModelSpaceDimensionLinearScales` устанавливает `Dimlfac = 1.0` в стилях, сохраняет экземплярные `Dimlfac`, затем пересчитывает размеры через `RecomputeDimensionBlock(true)`.

## 5. Вставка в целевой чертеж

*Реализовано в `BlockInserter.InsertNativeObjects`.*

1. `CombineOrchestrator` берёт `DocumentLock` на целевой документ.
2. `ExtentsUtils.SyncUnits` приводит target к мм/метрика.
3. `WblockCloneObjects` оборачивается в `DatabaseUnitSyncScope`: единицы target временно выравниваются с source, чтобы AutoCAD не применял скрытое масштабирование (метрика ↔ имперская).
4. Model Space entities подготовленной source DB клонируются через `WblockCloneObjects` с `DuplicateRecordCloning.Ignore`.
5. К каждому клонированному объекту применяется displacement.
6. Следующий лист размещается справа от предыдущего; зазор равен `Max(1.0, Round(Max(width, height) * gapPercent, 0))`.
7. `DimensionStyleDiagnosticUtils.LogStyleSnapshot` пишет снимок `target-after-clone`.

## 6. Финализация

1. `RasterImagePathFixer.CopyImagesToTargetFolder` копирует растры рядом с итоговым DWG и обновляет пути.
2. `DimensionStyleDiagnosticUtils.LogStyleSnapshot` пишет снимок `target-after-merge`.
3. `DwgOptimizer.Optimize` выполняет до 5 проходов `Purge`.
4. Итоговый файл сохраняется через `SaveAs(savePath, DwgVersion.AC1032)`.
5. AutoCAD получает команды `REGENALL` и `ZOOM EXTENTS`.

## 7. Диагностический headless-сценарий

`tools/Run-MergeDwgDiagTest.ps1` строит core bundle с `/p:CoreConsoleDiagnostics=true`, создает script file для `accoreconsole.exe` и вызывает `MERGEDWG_DIAG_TEST`.

В текущих C# исходниках нет `[CommandMethod("MERGEDWG_DIAG_TEST")]`, поэтому сценарий не является рабочим acceptance test до восстановления команды или изменения скрипта.
