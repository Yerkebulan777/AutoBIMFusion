# Техническая документация AutoBIMFusion

**Последнее обновление:** 2026-05-10

## 1. Обзор

AutoBIMFusion — плагин AutoCAD .NET для AutoCAD 2025-2027. Решение разбито на несколько проектов, но runtime-поведение активной команды `MERGEDWG` сохранено. A25/A26 собираются под `net8.0`; A27 собирается под `net10.0`, потому что `AutoCAD.NET 26.x` поддерживает только `net10.0`.

Публичные артефакты автозагрузки не менялись:

- bundle: `AutoBIMFusion.bundle`
- plugin DLL: `AutoBIMFusion.dll`
- manifest path: `./Contents/AutoBIMFusion.dll`

Команды очистки текстов, текстовых стилей, объединения линий и создания eTransmit ZIP-пакетов архивированы и исключены из сборки.

## 2. Структура решения

```text
src/
  AutoBIMFusion.Plugin/
    AutoBIMFusionExtension.cs
    Commands/
      CombineCommands.cs
      Archive/
    Ribbon/
    Resources/
  AutoBIMFusion.Merge/
    Combine/
      Layouts/
  AutoBIMFusion.AutoCAD/
    AcadSupport/
    FileUtil.cs
    LayoutUtil.cs
    UiDialogService.cs
    WindowsNaturalComparer.cs
  AutoBIMFusion.Infrastructure/
    Logging/
tests/
  AutoBIMFusion.Tests/
docs/
```

Роли проектов:

| Проект | Тип | Назначение |
|---|---|---|
| `AutoBIMFusion.Plugin` | AutoCAD plugin | Entry point, `[CommandMethod]`, Ribbon, Resources, bundle/deploy MSBuild targets |
| `AutoBIMFusion.Merge` | Class library | Pipeline объединения DWG, layout projection, dimensions, raster path fixing, optimizer |
| `AutoBIMFusion.AutoCAD` | Class library | Общие AutoCAD helpers, file/layout helpers, system-variable scopes |
| `AutoBIMFusion.Infrastructure` | Class library | Serilog wiring и инфраструктурные сервисы |
| `AutoBIMFusion.Tests` | Executable | Smoke-test для алгоритмов без запуска AutoCAD UI |

## 3. Зависимости

```text
AutoBIMFusion.Plugin
  -> AutoBIMFusion.Merge
  -> AutoBIMFusion.AutoCAD

AutoBIMFusion.Plugin
  -> AutoBIMFusion.Infrastructure

AutoBIMFusion.Merge
  -> AutoBIMFusion.AutoCAD

AutoBIMFusion.Tests
  -> AutoBIMFusion.Merge
```

`MERGEDWG` остается в `AutoBIMFusion.Plugin`, чтобы AutoCAD стабильно обнаруживал команду в основном plugin assembly.

## 4. Команды

| Команда | Класс | Назначение | Ribbon |
|---|---|---|---|
| `MERGEDWG` | `AutoBIMFusion.Plugin.Commands.CombineCommands` | Объединение DWG из выбранной папки | Да |

Архивные команды `SMART_MERGE_TEXT`, `MERGE_TEXT_STYLES`, `JOIN_LINES` и `CREATE_ETRANSMIT_ZIP` находятся в `src/AutoBIMFusion.Plugin/Commands/Archive` и исключены из компиляции через `Compile Remove`.

`MERGEDWG_DIAG_TEST` не зарегистрирована. Скрипт `tools/Run-MergeDwgDiagTest.ps1` все еще вызывает эту команду; это известная нерабочая диагностика, не acceptance gate.

## 5. Ключевые классы

| Класс | Проект | Назначение |
|---|---|---|
| `CombineCommands` | `AutoBIMFusion.Plugin` | Точка входа `MERGEDWG`; выбор папки, семафор, progress meter, финализация и сохранение |
| `CombineOrchestrator` | `AutoBIMFusion.Merge` | Обработка одного DWG: validation, подготовка source DB, вставка |
| `ViewportLayoutExporter` | `AutoBIMFusion.Merge` | Открывает DWG в фоновой `Database(false, true)` и готовит Model Space к слиянию |
| `LayoutProjectionProcessor` | `AutoBIMFusion.Merge` | Перенос Paper Space в Model Space, main/aux vpt projection, scale clamp |
| `ViewportTransformer` | `AutoBIMFusion.Merge` | Матрицы трансформации, clone/transform, erase outside main VP, draw order |
| `DimensionStyleNormalizer` | `AutoBIMFusion.Merge` | Очистка DSTYLE overrides и назначение чистого AutoBIM-стиля скопированным размерам |
| `DimensionStyleDiagnosticUtils` | `AutoBIMFusion.Merge` | Диагностические снимки размерных и текстовых стилей |
| `BlockInserter` | `AutoBIMFusion.Merge` | `WblockCloneObjects` + расстановка по оси X |
| `RasterImagePathFixer` | `AutoBIMFusion.Merge` | Копирование растров и перевод путей в относительные |
| `DwgOptimizer` | `AutoBIMFusion.Merge` | Многопроходный `Database.Purge` |
| `FileUtil` | `AutoBIMFusion.AutoCAD` | DWG enumeration, natural sort, file validation |
| `LayoutUtil` | `AutoBIMFusion.AutoCAD` | Поиск Paper Space layouts и layout helpers |
| `LoggerFactory` | `AutoBIMFusion.Infrastructure` | Общий Serilog logger для plugin runtime |

## 6. Merge pipeline

Фактический путь выполнения:

```text
CombineCommands
  -> FileUtil
  -> CombineOrchestrator
  -> ViewportLayoutExporter / LayoutProjectionProcessor
  -> BlockInserter
  -> DimensionStyleNormalizer
  -> RasterImagePathFixer
  -> DwgOptimizer
  -> SaveAs(DwgVersion.AC1032)
```

Подготовка каждого исходного DWG выполняется в фоновой `Database(false, true)` после `ReadDwgFile` и `CloseInput(true)`. Временный DWG-файл не создается. `ExtentsUtils.SyncUnits` задает `Insunits = Millimeters` и `Measurement = Metric`; `MEASUREINIT` не меняется, потому что это registry-переменная для новых чертежей.

Перед `WblockCloneObjects` `DatabaseUnitSyncScope` временно приравнивает `Insunits`, `Measurement` и `Dimalt` исходной базы к целевой. После клонирования `DimensionStyleNormalizer.NormalizeClonedDimensions` назначает скопированным размерам чистый стиль AutoBIM, очищает DSTYLE overrides из XData/ExtensionDictionary, сохраняет экземплярные `Dimscale`/`Dimlfac` и пересчитывает dimension blocks.

## 7. Сборка и пакеты

- Решение: `AutoBIMFusion.slnx`.
- Конфигурации: `DebugA25`, `DebugA26`, `DebugA27`, `ReleaseA25`, `ReleaseA26`, `ReleaseA27`.
- Target framework: A25/A26 -> `net8.0`; A27 -> `net10.0`.
- `AutoCAD.NET`: floating `$(AcadPackageVersion).*`.
- `AutoCAD.NET.Interop`: floating `$(AcadInteropPackageVersion).*`.
- Serilog: `4.0.0`; `Serilog.Sinks.File`: `6.0.0`.
- Runtime dependencies Serilog копируются в bundle.
- AutoCAD host DLLs не должны копироваться как runtime assets.

Только `AutoBIMFusion.Plugin` создает и деплоит `.bundle`. В `AutoBIMFusion.Plugin.csproj` задан `AssemblyName=AutoBIMFusion`, чтобы итоговый DLL оставался `AutoBIMFusion.dll`.

`CoreConsoleDiagnostics=true` добавляет `CORECONSOLE_DIAGNOSTICS`, исключает `AutoBIMFusionExtension.cs`, весь `Ribbon/**` и `Microsoft.WindowsDesktop.App`. Архивные команды в `Commands/Archive/**` исключаются из сборки всегда.

## 8. Управление ресурсами

- Любая запись в активный документ выполняется внутри `using (doc.LockDocument())`.
- Транзакции создаются через `TransactionManager.StartTransaction()` и завершаются `Commit()`.
- Фоновые `Database` уничтожаются через `using`.
- `AcadWarningSuppressScope` восстанавливает системные переменные AutoCAD через RAII; `DatabaseUnitSyncScope` синхронизирует `Insunits`/`Measurement`/`Dimalt` source с target на время `WblockCloneObjects`.
- AutoCAD API остается на основном потоке; API не потокобезопасен.

## 9. Логирование

- Основной логгер: `LoggerFactory.GetSharedLogger()`.
- Активная команда пишет в `%AppData%\Autodesk\ApplicationPlugins\AutoBIMFusion.bundle\Contents\Logs\merge-YYYY-MM-DD.log`.
- `DiagnosticSink` дублирует сообщения в `Debug.WriteLine` или `Trace.WriteLine`.
- Размерные стили диагностируются стадиями `source-after-normalize-before-clone`, `target-after-clone` и `target-after-merge`.
