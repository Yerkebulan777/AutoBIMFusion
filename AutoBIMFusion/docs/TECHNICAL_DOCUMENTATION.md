# Техническая документация AutoBIMFusion

**Последнее обновление:** 2026-05-05

## 1. Обзор

AutoBIMFusion — плагин AutoCAD .NET для AutoCAD 2025-2027. Проект `AutoBIMFusion.csproj` собирается как `net8.0`, `x64`; общий `Directory.Build.props` содержит `net10.0-windows`, но проект переопределяет TargetFramework.

Плагин автоматизирует объединение DWG, очистку текстов и текстовых стилей, объединение линий и создание eTransmit ZIP-пакетов.

## 2. Структура проекта

```text
AutoBIMFusion/
  Application/
    AcadSupport/      # RAII-скоупы системных переменных AutoCAD
    Commands/         # Точки входа AutoCAD CommandMethod
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
| `SMART_MERGE_TEXT` | `SmartTextCommands` | Объединение близких `TEXT` / `MTEXT` в один `MText` | Да |
| `MERGE_TEXT_STYLES` | `TextStyleCommands` | Слияние дублирующихся текстовых стилей | Да |
| `JOIN_LINES` | `JoinCommands` | Склейка коллинеарных коротких `LINE` | Да |
| `CREATE_ETRANSMIT_ZIP` | `TransmittalCommands` | Создание ZIP-пакета eTransmit | Нет |

`MERGEDWG_DIAG_TEST` не зарегистрирована в текущем коде. Скрипт `tools/Run-MergeDwgDiagTest.ps1` все еще вызывает эту команду; это отражено в известных проблемах.

## 4. Ключевые классы

| Класс | Назначение |
|---|---|
| `CombineCommands` | Точка входа `MERGEDWG`; выбор папки, семафор, progress meter, финализация и сохранение |
| `CombineOrchestrator` | Обработка одного DWG: validation, подготовка source DB, вставка |
| `ViewportLayoutExporter` | Открывает DWG в фоновой `Database(false, true)` и готовит Model Space к merge |
| `LayoutProjectionProcessor` | Перенос Paper Space в Model Space, main/aux vpt projection, scale clamp |
| `ViewportTransformer` | Матрицы трансформации, clone/transform, erase outside main VP, draw order |
| `DimensionStyleNormalizer` | Нормализация размерных стилей до клонирования |
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

Подготовка каждого исходного DWG выполняется в фоновой `Database(false, true)` после `ReadDwgFile` и `CloseInput(true)`. Временный DWG-файл для подготовки не создается.

## 6. Сборка и пакеты

- Решение: `AutoBIMFusion.slnx`.
- Конфигурации: `DebugA25`, `DebugA26`, `DebugA27`, `ReleaseA25`, `ReleaseA26`, `ReleaseA27`.
- `AutoCAD.NET`: floating `$(AcadPackageVersion).*`.
- `AutoCAD.NET.Interop`: floating `$(AcadInteropPackageVersion).*`.
- Serilog: `4.0.0`; `Serilog.Sinks.File`: `6.0.0`.
- Runtime dependencies Serilog копируются в bundle; AutoCAD host DLLs не должны копироваться как runtime assets.

`CoreConsoleDiagnostics=true` добавляет `CORECONSOLE_DIAGNOSTICS`, исключает `AutoBIMFusionExtension.cs`, весь `Application/Ribbon/**` и `Microsoft.WindowsDesktop.App`.

## 7. Управление ресурсами

- Любая запись в активный документ выполняется внутри `using (doc.LockDocument())`.
- Транзакции создаются через `TransactionManager.StartTransaction()` и завершаются `Commit()`.
- Фоновые `Database` уничтожаются через `using`.
- `AcadWarningSuppressScope` и `AcadUnitScalingOverrideScope` восстанавливают системные переменные AutoCAD через RAII.
- AutoCAD API остается на основном потоке; API не потокобезопасен.

## 8. Логирование

- Основной логгер: `LoggerFactory.GetSharedLogger()` или `LoggerFactory.CreateLoggerInDirectory(sourceFolder)`.
- Команда `MERGEDWG` пишет лог рядом с выбранной исходной папкой.
- `DiagnosticSink` дублирует сообщения в `Debug.WriteLine` или `Trace.WriteLine`.
- Размерные стили диагностируются стадиями `before-normalize`, `after-normalize`, `after-merge`.
