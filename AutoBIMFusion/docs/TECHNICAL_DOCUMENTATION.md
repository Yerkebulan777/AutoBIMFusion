# Техническая документация AutoBIMFusion

**Последнее обновление:** 2026-05-06

## 1. Обзор

AutoBIMFusion — плагин AutoCAD .NET для AutoCAD 2025-2027. Проект `AutoBIMFusion.csproj` собирается как `net8.0`, `x64`; общий `Directory.Build.props` содержит `net10.0-windows`, но проект переопределяет TargetFramework.

Активная команда плагина объединяет DWG-файлы. Команды очистки текстов, текстовых стилей, объединения линий и создания eTransmit ZIP-пакетов временно архивированы.

## 2. Структура проекта

```text
AutoBIMFusion/
  Application/
    AcadSupport/      # RAII-скоупы системных переменных AutoCAD
    Commands/         # Активные точки входа AutoCAD CommandMethod
      Archive/        # Архивные команды, исключенные из сборки
    Combine/          # Пайплайн объединения DWG
      Layouts/        # Layout, vpt, трансформации, размеры, extents
    Ribbon/           # Ribbon-кнопки; исключается при CoreConsoleDiagnostics=true
    Utils/            # Файлы, строки, диалоги, layout helpers, natural sort
  Infrastructure/
    Logging/          # Serilog + DiagnosticSink
```

## 3. Команды

| Команда | Класс | Назначение | Ribbon |
|---|---|---|---|
| `MERGEDWG` | `CombineCommands` | Пакетное объединение DWG из выбранной папки | Да |

Архивные команды `SMART_MERGE_TEXT`, `MERGE_TEXT_STYLES`, `JOIN_LINES` и `CREATE_ETRANSMIT_ZIP` находятся в `Application/Commands/Archive` и исключены из компиляции через `AutoBIMFusion.csproj`.

`MERGEDWG_DIAG_TEST` не зарегистрирована в текущем коде. Скрипт `tools/Run-MergeDwgDiagTest.ps1` все еще вызывает эту команду; это отражено в известных проблемах.

## 4. Ключевые классы

| Класс | Назначение |
|---|---|
| `CombineCommands` | Точка входа `MERGEDWG`; выбор папки, семафор, progress meter, финализация и сохранение |
| `CombineOrchestrator` | Обработка одного DWG: validation, подготовка source DB, вставка |
| `ViewportLayoutExporter` | Открывает DWG в фоновой `Database(false, true)` и готовит Model Space к merge |
| `LayoutProjectionProcessor` | Перенос Paper Space в Model Space, main/aux vpt projection, scale clamp |
| `ViewportTransformer` | Матрицы трансформации, clone/transform, erase outside main VP, draw order |
| `DimensionStyleNormalizer` | Создание Viewport-специфичного размерного стиля для текущего размера |
| `DimensionStyleDiagnosticUtils` | Диагностические снимки размерных и текстовых стилей |
| `BlockInserter` | `WblockCloneObjects` + расстановка по оси X |
| `RasterImagePathFixer` | Копирование растров и перевод путей в относительные |
| `DwgOptimizer` | Многопроходный `Database.Purge` |
| `FileUtil` | DWG enumeration, natural sort, file validation |
| `ExtentsUtils` | Extents math и синхронизация единиц |

## 5. Merge pipeline

Фактический путь выполнения:

```text
CombineCommands
  -> FileUtil
  -> CombineOrchestrator
  -> ViewportLayoutExporter / LayoutProjectionProcessor
  -> DimensionStyleNormalizer
  -> BlockInserter
  -> RasterImagePathFixer
  -> DwgOptimizer
  -> SaveAs(DwgVersion.AC1032)
```

Подготовка каждого исходного DWG выполняется в фоновой `Database(false, true)` после `ReadDwgFile` и `CloseInput(true)`. Временный DWG-файл для подготовки не создается. `ExtentsUtils.SyncUnits` задает `Insunits = Millimeters` и `Measurement = Metric`; `MEASUREINIT` не меняется, потому что это registry-переменная для новых чертежей.

Во время обработки каждого Viewport `ViewportTransformer.NormalizeDimensionsInsideViewport` назначает видимым Model Space размерам стиль `{OldName}_{Scale}` до aux-клонирования и трансформации. Если главный VP зажат до рабочего масштаба 1:100, суффикс и визуальные параметры стиля рассчитываются по итоговому множителю с учетом `clampRatio`. `DimensionStyleNormalizer.NormalizeDimensionStyleForViewport` клонирует текущий `DimStyleTableRecord`, запекает исходный `Dimscale` в визуальные параметры (`Dimtxt`, `Dimasz`, `Dimgap` и др.) и задает стилю `Dimscale = 1.0`. После трансформаций `ViewportTransformer.FinalizeModelSpaceDimensionLinearScales` очищает DSTYLE overrides, задает `Dimlfac = 1.0` размерам и их стилям, затем пересчитывает dimension blocks.

## 6. Сборка и пакеты

- Решение: `AutoBIMFusion.slnx`.
- Конфигурации: `DebugA25`, `DebugA26`, `DebugA27`, `ReleaseA25`, `ReleaseA26`, `ReleaseA27`.
- `AutoCAD.NET`: floating `$(AcadPackageVersion).*`.
- `AutoCAD.NET.Interop`: floating `$(AcadInteropPackageVersion).*`.
- Serilog: `4.0.0`; `Serilog.Sinks.File`: `6.0.0`.
- Runtime dependencies Serilog копируются в bundle; AutoCAD host DLLs не должны копироваться как runtime assets.

`CoreConsoleDiagnostics=true` добавляет `CORECONSOLE_DIAGNOSTICS`, исключает `AutoBIMFusionExtension.cs`, весь `Application/Ribbon/**` и `Microsoft.WindowsDesktop.App`. Архивные команды в `Application/Commands/Archive/**` исключаются из сборки всегда.

## 7. Управление ресурсами

- Любая запись в активный документ выполняется внутри `using (doc.LockDocument())`.
- Транзакции создаются через `TransactionManager.StartTransaction()` и завершаются `Commit()`.
- Фоновые `Database` уничтожаются через `using`.
- `AcadWarningSuppressScope` и `AcadUnitScalingOverrideScope` восстанавливают системные переменные AutoCAD через RAII.
- AutoCAD API остается на основном потоке; API не потокобезопасен.

## 8. Логирование

- Основной логгер: `LoggerFactory.GetSharedLogger()`.
- Все активные команды пишут в `%AppData%\Autodesk\ApplicationPlugins\AutoBIMFusion.bundle\Contents\Logs\merge-YYYY-MM-DD.log`.
- `DiagnosticSink` дублирует сообщения в `Debug.WriteLine` или `Trace.WriteLine`.
- Размерные стили диагностируются стадиями `source-after-normalize-before-clone` и `after-merge`; нормализация Viewport пишет отдельные debug/info-сообщения.
