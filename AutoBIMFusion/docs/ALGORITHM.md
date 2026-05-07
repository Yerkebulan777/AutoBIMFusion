# Алгоритм работы AutoBIMFusion

**Последнее обновление:** 2026-05-07

## 1. Запуск и выбор файлов

1. Команда `MERGEDWG` входит в `CombineCommands.MergeDwgFolderCommand`.
2. `SemaphoreSlim` блокирует повторный запуск параллельной merge-операции.
3. Пользователь выбирает папку через `UiDialogService.TrySelectFolder`.
4. `FileUtil.GetFiles` собирает `.dwg` до 3 уровней вложенности, пропуская имена с префиксом `#` и файлы больше 15 МБ.
5. Файлы сортируются естественным порядком через `WindowsNaturalComparer`.

## 2. Обработка одного DWG

*Реализовано в `CombineOrchestrator.MergeSingleFile`.*

1. `FileUtil.TryValidateDwg` проверяет наличие, ненулевой размер и возможность открыть файл как DWG.
2. `ViewportLayoutExporter.PrepareDatabaseForMerge` открывает файл в фоновой базе `new Database(false, true)`.
3. После `ReadDwgFile` вызывается `CloseInput(true)`.
4. База переводится в метрические единицы: `Insunits = Millimeters`, `Measurement = Metric`; `MEASUREINIT` не меняется, потому что это registry-переменная для новых чертежей. Единицы не-xref block table records также приводятся к millimeters.
5. Выбирается первый Layout по `TabOrder`, отличный от Model Space.
6. `ViewportCollector` собирает viewports выбранного листа.

## 3. Проекция Layout в Model Space

*Реализовано в `LayoutProjectionProcessor`.*

### Без видовых экранов

1. Paper Space entities выбранного Layout клонируются в Model Space.
2. Применяется масштаб по умолчанию `1:100`.
3. Исходное содержимое Paper Space очищается.

### С видовыми экранами

1. Главный vpt выбирается через `ViewportInfo.PickMainViewport`.
2. Рабочий масштаб main vpt зажимается до `1:100` для более мелких масштабов.
3. Для общей геометрии используется `clampRatio`; матрицы aux/main строятся из параметров исходных vpt.
4. Размеры, видимые в конкретном vpt, до клонирования получают стиль `{OldName}_{Scale}` и экземплярный `Dimlfac`.
5. Aux viewports клонируют и трансформируют свои Model Space объекты в координаты main vpt.
6. После aux-клонирования исходные размеры, оставшиеся в main window, повторно получают стиль main vpt.
7. Остатки объектов aux viewports за пределами main window удаляются.
8. Paper Space переносится в Model Space через матрицу `BuildPaperToMainMatrix`.

## 4. Тримминг и размеры

1. Если рассчитана рамка листа, `ModelSpaceTrimmer.TrimOutside` удаляет объекты вне рамки.
2. `DimensionStyleNormalizer.NormalizeDimensionStyleForViewport` клонирует текущий стиль размера, если стиль `{OldName}_{Scale}` еще не создан; при clamp VP суффикс использует итоговый рабочий множитель.
3. Перед установкой `Dimscale = 1.0` визуальные параметры (`Dimtxt`, `Dimasz`, `Dimgap` и др.) умножаются на исходный `Dimscale` с учетом `clampRatio`; при некорректном `Dimscale` используется масштаб vpt. Стиль отвечает только за визуальный размер.
4. DSTYLE overrides не возникают: `DatabaseUnitSyncScope` устраняет первопричину (см. `Application/AcadSupport/DatabaseUnitSyncScope.cs`).
5. Экземпляр размера отвечает за числовую поправку измерения: при clamp Model Space получает `Dimlfac = 1 / clampRatio`, без clamp ожидается `Dimlfac = 1.0`.
6. После трансформаций используемые размерные стили получают `Dimlfac = 1.0`, экземплярные `Dimlfac` сохраняются, затем размеры пересчитываются через `RecomputeDimensionBlock(true)`.
7. Диагностика пишет снимок `source-after-normalize-before-clone`.

## 5. Вставка в целевой чертеж

*Реализовано в `BlockInserter.InsertNativeObjects`.*

1. `CombineOrchestrator` берет `DocumentLock` на целевой документ.
2. `ExtentsUtils.SyncUnits` приводит target к мм/метрика.
3. `WblockCloneObjects` оборачивается в `DatabaseUnitSyncScope`: единицы target временно выравниваются с source, чтобы AutoCAD не применял скрытое масштабирование (метрика ↔ имперская).
4. Model Space entities подготовленной source DB клонируются через `WblockCloneObjects` с `DuplicateRecordCloning.Ignore`.
5. К каждому клону применяется displacement.
6. Следующий лист размещается справа от предыдущего; зазор равен 10% от максимального габарита, но не меньше 1.0.

## 6. Финализация

1. `RasterImagePathFixer.CopyImagesToTargetFolder` копирует растры рядом с итоговым DWG и обновляет пути.
2. `DimensionStyleDiagnosticUtils.LogStyleSnapshot` пишет снимок `after-merge`.
3. `DwgOptimizer.Optimize` выполняет до 5 проходов `Purge`.
4. Итоговый файл сохраняется через `SaveAs(savePath, DwgVersion.AC1032)`.
5. AutoCAD получает команды `REGENALL` и `ZOOM EXTENTS`.

## 7. Диагностический headless-сценарий

`tools/Run-MergeDwgDiagTest.ps1` строит core bundle с `/p:CoreConsoleDiagnostics=true`, создает script file для `accoreconsole.exe` и вызывает `MERGEDWG_DIAG_TEST`.

В текущих C# исходниках нет `[CommandMethod("MERGEDWG_DIAG_TEST")]`, поэтому сценарий не является рабочим acceptance test до восстановления команды или изменения скрипта.
