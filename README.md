# AutoBIMFusion

AutoBIMFusion — плагин AutoCAD .NET для AutoCAD 2025-2027.

Активная команда `MERGEDWG` объединяет DWG-файлы из выбранной папки в текущий чертеж. Импортированная геометрия переносится в Model Space как редактируемые нативные объекты AutoCAD.

Публичные артефакты автозагрузки сохранены: bundle называется `AutoBIMFusion.bundle`, plugin assembly называется `AutoBIMFusion.dll`, а `PackageContents.xml` продолжает ссылаться на `./Contents/AutoBIMFusion.dll`.

Конфигурации A25/A26 собираются под `net8.0`; A27 собирается под `net10.0`, потому что `AutoCAD.NET 26.x` не поддерживает `net8.0`.

## Структура

```text
src/
  AutoBIMFusion.Plugin/         # AutoCAD entrypoint, MERGEDWG, Ribbon, Resources, bundle/deploy targets
  AutoBIMFusion.Merge/          # DWG merge pipeline: layouts, extents, dimensions, optimizer
  AutoBIMFusion.AutoCAD/        # Общие AutoCAD helpers и scope-обертки
  AutoBIMFusion.Infrastructure/ # Logging и инфраструктурные сервисы
tests/
  AutoBIMFusion.Tests/          # Executable smoke tests
docs/
  TECHNICAL_DOCUMENTATION.md
  ALGORITHM.md
  KNOWN_ISSUES.md
  PROJECT_STRUCTURE.md
```

Зависимости идут в одну сторону:

```text
AutoBIMFusion.Plugin
  -> AutoBIMFusion.Merge
  -> AutoBIMFusion.AutoCAD

AutoBIMFusion.Plugin
  -> AutoBIMFusion.Infrastructure

AutoBIMFusion.Tests
  -> AutoBIMFusion.Merge
```

Архивные команды `SMART_MERGE_TEXT`, `MERGE_TEXT_STYLES`, `JOIN_LINES` и `CREATE_ETRANSMIT_ZIP` находятся в `src/AutoBIMFusion.Plugin/Commands/Archive` и исключены из компиляции. AutoCAD их не регистрирует.

## Команды

| Команда | Назначение | Ribbon |
| :--- | :--- | :--- |
| `MERGEDWG` | Рекурсивно находит DWG, экспортирует первый Layout каждого файла в Model Space и вставляет результат в текущий чертеж. | Да |

`tools/Run-MergeDwgDiagTest.ps1` вызывает `MERGEDWG_DIAG_TEST`, но такой команды в C# сейчас нет. Скрипт считается известной нерабочей диагностикой, а не acceptance gate.

## Процесс слияния

1. `CombineCommands`: запускает `MERGEDWG`, выбирает папку, блокирует параллельный запуск, показывает прогресс и сохраняет результат.
2. `FileUtil`: собирает `.dwg` до 3 уровней вложенности, пропускает файлы с префиксом `#` и файлы больше 15 МБ, сортирует естественным порядком.
3. `CombineOrchestrator`: проверяет DWG, готовит фоновую базу и вставляет результат в целевой чертеж.
4. `ViewportLayoutExporter` / `LayoutProjectionProcessor`: открывают исходный DWG через `Database(false, true)`, переводят базу в millimeters/metric и переносят первый Paper Space Layout в Model Space.
5. `DimensionStyleNormalizer`: назначает скопированным размерам чистый AutoBIM-стиль, очищает DSTYLE overrides и сохраняет визуальный масштаб размеров.
6. `BlockInserter`: клонирует объекты через `WblockCloneObjects` и раскладывает листы по оси X с зазором 10%.
7. Финализация: копирование растров, снимок размерных стилей, `DwgOptimizer`, `SaveAs(DwgVersion.AC1032)`, `REGENALL`, `ZOOM EXTENTS`.

Лог пишется в `%AppData%\Autodesk\ApplicationPlugins\AutoBIMFusion.bundle\Contents\Logs\merge-YYYY-MM-DD.log`.

## Сборка

Решение использует новый XML-формат `.slnx`; `dotnet build` поддерживает его напрямую.

```powershell
dotnet build AutoBIMFusion.slnx -c DebugA26
dotnet build AutoBIMFusion.slnx -c ReleaseA26
dotnet clean AutoBIMFusion.slnx -c DebugA26
```

Доступные конфигурации: `DebugA25`, `DebugA26`, `DebugA27`, `ReleaseA25`, `ReleaseA26`, `ReleaseA27`.

Только `src/AutoBIMFusion.Plugin` создает и разворачивает `.bundle` в `%AppData%\Autodesk\ApplicationPlugins\AutoBIMFusion.bundle`. Остальные проекты являются class library и не деплоят AutoCAD bundle.

Headless/core-console сборка:

```powershell
dotnet build AutoBIMFusion.slnx -c DebugA26 /p:CoreConsoleDiagnostics=true
```

`CoreConsoleDiagnostics=true` исключает `AutoBIMFusionExtension.cs`, весь `Ribbon/` и WPF framework reference.

## Smoke test

```powershell
dotnet run --project tests/AutoBIMFusion.Tests/AutoBIMFusion.Tests.csproj -c DebugA26
```

Тестовый проект сейчас является executable smoke-test и ссылается на `AutoBIMFusion.Merge`. Для внутренних алгоритмов `AutoBIMFusion.Merge` открыт friend access через `InternalsVisibleTo("AutoBIMFusion.Tests")`.

## Версии пакетов

| AutoCAD | Config suffix | `AcadPackageVersion` | `AcadInteropPackageVersion` | Preprocessor |
|---|---|---|---|---|
| 2025 | A25 | `25.0` | `2025.0` | `ACAD2025` |
| 2026 | A26 | `25.1` | `2026.0` | `ACAD2026` |
| 2027 | A27 | `26.0` | `2026.0` | `ACAD2027` |

Версии NuGet централизованы в `Directory.Packages.props`:

- `AutoCAD.NET`: `$(AcadPackageVersion).*`
- `AutoCAD.NET.Interop`: `$(AcadInteropPackageVersion).*`
- `Serilog`: `4.0.0`
- `Serilog.Sinks.File`: `6.0.0`

AutoCAD host DLLs не копируются в output и bundle: AutoCAD NuGet references используют `ExcludeAssets="runtime"`.

## Документация

- [Структура проекта](docs/PROJECT_STRUCTURE.md)
- [Техническое описание](docs/TECHNICAL_DOCUMENTATION.md)
- [Алгоритм слияния](docs/ALGORITHM.md)
- [Известные проблемы](docs/KNOWN_ISSUES.md)
