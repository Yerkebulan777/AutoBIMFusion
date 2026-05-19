# Техническая документация AutoBIMFusion

**Последнее обновление:** 2026-05-19

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
      OutOfFrameEntityCleaner.cs
      Layouts/
  AutoBIMFusion.Common/
    AcadSupport/
    Compatibility/
    Drawing/
    Extensions/
    Helpers/
    Mist/
    UiDialogService.cs
    Logging/
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
| `AutoBIMFusion.Common` | Class library | Общие AutoCAD helpers, file/layout helpers, system-variable scopes |
| `AutoBIMFusion.Infrastructure` | Class library | Serilog wiring и инфраструктурные сервисы |
| `AutoBIMFusion.Tests` | Executable | Smoke-test для алгоритмов без запуска AutoCAD UI |

## 3. Зависимости

```text
AutoBIMFusion.Plugin
  -> AutoBIMFusion.Merge
  -> AutoBIMFusion.Common

AutoBIMFusion.Plugin
  -> AutoBIMFusion.Infrastructure

AutoBIMFusion.Merge
  -> AutoBIMFusion.Common

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
| `DimensionStyleDiagnosticUtils` | `AutoBIMFusion.Merge` | Диагностические снимки размерных стилей, включаются при `LOG_LEVEL=DEBUG` |
| `BlockInserter` | `AutoBIMFusion.Merge` | `WblockCloneObjects` + расстановка по оси X |
| `OutOfFrameEntityCleaner` | `AutoBIMFusion.Merge` | Удаление малых объектов, центр bbox которых находится за рамкой листа |
| `BlockBasePointEditor` | `AutoBIMFusion.Merge` | Нормализация базовых точек блоков в левый нижний угол (offset compensation) |
| `BlockScaleApplier` | `AutoBIMFusion.Merge` | Нормализация non-uniform block scale к 1.0 (definition + inverse reference scaling) |
| `StyleUnificationService` | `AutoBIMFusion.Merge` | Переименование text styles + применение GOST параметров ко всем dimension styles |
| `DrawOrderPreserver` | `AutoBIMFusion.Merge` | Capture/restore SortentsTable draw order при клонировании между BTR |
| `EntityTransformUtils` | `AutoBIMFusion.Common` | Post-transform processing: associative hatch skip, hatch evaluate, dimension text reset, attribute alignment |
| `RasterImagePathFixer` | `AutoBIMFusion.Merge` | Копирование растров и перевод путей в относительные |
| `DrawingPurger` | `AutoBIMFusion.Merge` | Многопроходный `Database.Purge` |
| `FileUtil` | `AutoBIMFusion.Common` | DWG enumeration, natural sort, file validation |
| `LayoutUtil` | `AutoBIMFusion.Common` | Поиск Paper Space layouts и layout helpers |
| `LoggerFactory` | `AutoBIMFusion.Infrastructure` | Общий Serilog logger для plugin runtime |

## 6. Merge pipeline

Фактический путь выполнения:

```text
CombineCommands
  -> FileUtil
  -> CombineOrchestrator
  -> ViewportLayoutExporter / LayoutProjectionProcessor
  -> OutOfFrameEntityCleaner
  -> BlockBasePointEditor
  -> BlockScaleApplier
  -> BlockInserter
     -> StyleUnificationService (before WblockCloneObjects)
     -> WblockCloneObjects
  -> DimensionStyleNormalizer
  -> RasterImagePathFixer
  -> DrawingPurger.Optimize
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

## 8. Формат сохранения

Результат объединения сохраняется в формате `DwgVersion.AC1032` (AutoCAD 2018). Это самый стабильный и широко поддерживаемый формат DWG, обеспечивающий максимальную совместимость с другими приложениями и версиями AutoCAD. Новые типы объектов, появившиеся в AutoCAD 2025-2027, могут быть упрощены или конвертированы при сохранении.

## 9. Управление ресурсами

- Любая запись в активный документ выполняется внутри `using (doc.LockDocument())`.
- Транзакции создаются через `TransactionManager.StartTransaction()` и завершаются `Commit()`.
- Фоновые `Database` уничтожаются через `using`.
- `AcadWarningSuppressScope` восстанавливает системные переменные AutoCAD через RAII — сохраняет оригинальные значения при создании и восстанавливает при Dispose; `DatabaseUnitSyncScope` синхронизирует `Insunits`/`Measurement`/`Dimalt` source с target на время `WblockCloneObjects`.
- AutoCAD API остается на основном потоке; API не потокобезопасен.

## 10. Логирование

- Основной логгер: `LoggerFactory.GetSharedLogger()`.
- Активная команда пишет в `%AppData%\Autodesk\ApplicationPlugins\AutoBIMFusion.bundle\Contents\Logs\merge-YYYY-MM-DD.log`.
- `DiagnosticSink` дублирует сообщения в `Debug.WriteLine` или `Trace.WriteLine`.
- Размерные стили диагностируются стадиями `source-after-normalize-before-clone`, `target-after-clone` и `target-after-merge` только при `LOG_LEVEL=DEBUG`.

## 11. Extensions

`AutoBIMFusion.Common/Extensions/` содержит 30 файлов extension methods для упрощения работы с AutoCAD API:

| Extension | Назначение |
|---|---|
| `ObjectIdExtensions` | `IsValidForOperation`, `EraseObject`, `GetDBObject` — безопасная работа с ObjectId |
| `EntityExtensions` | `IsEntityOnLockedLayer`, `TransformBySafe` — проверка слоёв и безопасная трансформация |
| `DatabaseExtensions` | `GetDocument`, `GetEditor` — получение Document/Editor из Database |
| `ViewportsExtensions` | `ResolveCustomScale`, `ComputeModelWindow`, `GetViewCenterWcs` — расчёт масштабов и окон viewport |
| `BlockReferenceExtensions` | `GetBlockReferenceName` — извлечение имени блока с поддержкой dynamic blocks |
| `DBObjectExtensions` | Общие методы для DBObject (erase, open, property access) |
| `PolylinesExtensions` | Утилиты для работы с Polyline/Polyline2d |
| `CurvesExtensions` | Методы для Curve-наследников (parameter, point at distance) |
| `HatchsExtensions` | Методы для Hatch (evaluate, loop management) |
| `CollectionsExtensions` | `Join`, `ToHashSet` и другие collection helpers |
| `DBTextExtensions` | Методы для DBText (alignment, formatting) |
| `Point3dExtensions` | Методы для Point3d (distance, angle, containment) |
| `Vector3dExtensions` | Методы для Vector3d (colinearity, rotation, projection) |
| `ColorsExtensions` | Цветовые утилиты (HSV, brightness, contrast, hex) |
| `StringExtensions` | Строковые утилиты (numeric extraction, diacritics, sanitization) |
| `ArcsExtensions` | Методы для Arc (circular arc conversion, bulge) |
| `LinesExtensions` | Методы для Line (intersection, vector, polyline conversion) |
| `Extends3dExtensions` | Методы для Extents3d (geometry, center, collision, zoom) |
| `RegionsExtensions` | Методы для Region (boolean operations, union, subtraction) |
| `CircularArcExtensions` / `CircularArc2dExtensions` / `CircularArc3dExtensions` | Методы для CircularArc типов |
| `ListExtensions` | Методы для List (remove common, sum numeric, deep dispose) |
| `IEnumerableExtensions` | Extension methods для IEnumerable |
| `Point3dCollectionExtensions` | Методы для Point3dCollection |
| `IntegerCollectionExtensions` | Методы для IntegerCollection |
| `DBObjectCollectionExtensions` | Методы для DBObjectCollection |
| `AttributeCollectionExtensions` | Методы для AttributeCollection |
| `BitmapExtensions` | Методы для Bitmap (image file size, rotation) |
| `ConcurrentBagExtensions` | Методы для ConcurrentBag |

Extensions централизуют повторяющуюся логику и обеспечивают единообразную обработку ошибок across the codebase.

## 13. Roslyn анализаторы и CI

С мая 2026 включены Roslyn анализаторы через `Directory.Build.props`:

- `AnalysisLevel=latest` — последние правила анализа
- `EnforceCodeStyleInBuild=true` — проверка стиля кода при сборке
- `EnableNETAnalyzers=true` — включение .NET analyzers
- `TreatWarningsAsErrors=false` — предупреждения не блокируют сборку

GitHub Actions workflows:
- `.github/workflows/format-check.yml` — проверка форматирования
- `.github/workflows/format.yml` — автоформатирование
- `.github/workflows/dead-code.yml` — поиск неиспользуемого кода

## 14. Drawing & Mist helpers

- `BlockReferences.cs` (694 строки) — comprehensive block utilities: создание, копирование, трансформация block references; работа с dynamic block properties; поиск и фильтрация блоков по имени/слою.
- `Entities.cs` — утилиты для создания и модификации AutoCAD entities (lines, polylines, circles, text, dimensions).

### Mist

- `Generic.cs` (281 строка) — central utility class: tolerances (`LowTolerance`, `HighTolerance`), system variable management, type conversion helpers, math utilities.
- `AutoCAD/` — AutoCAD-specific helpers (system variables, unit conversion, command invocation).
- `Geometry/` — geometric utilities (point/line/plane calculations, intersection tests, bounding box operations).
