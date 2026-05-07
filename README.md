# AutoBIMFusion

AutoBIMFusion — плагин AutoCAD .NET для AutoCAD 2025-2027.

Активная команда `MERGEDWG` объединяет DWG-файлы из выбранной папки в текущий чертеж. Импортированная геометрия переносится в Model Space как редактируемые нативные объекты AutoCAD.

Проект `AutoBIMFusion/AutoBIMFusion.csproj` собирается под `net8.0`, `x64`. Общий `Directory.Build.props` содержит `net10.0-windows`, но проект явно переопределяет `TargetFramework`.

## Команды

| Команда | Назначение | Ribbon |
| :--- | :--- | :--- |
| `MERGEDWG` | Рекурсивно находит DWG, экспортирует первый Layout каждого файла в Model Space и вставляет результат в текущий чертеж. | Да |

Команды `SMART_MERGE_TEXT`, `MERGE_TEXT_STYLES`, `JOIN_LINES` и `CREATE_ETRANSMIT_ZIP` находятся в `Application/Commands/Archive` и исключены из компиляции. AutoCAD их не регистрирует.

`tools/Run-MergeDwgDiagTest.ps1` вызывает `MERGEDWG_DIAG_TEST`, но такой команды в C# сейчас нет. Скрипт считается известной проблемой, а не acceptance test.

## Процесс слияния

1. `CombineCommands`: запускает `MERGEDWG`, выбирает папку, блокирует параллельный запуск, показывает прогресс и сохраняет результат.
2. `FileUtil`: собирает `.dwg` до 3 уровней вложенности, пропускает файлы с префиксом `#` и файлы больше 15 МБ, сортирует естественным порядком.
3. `CombineOrchestrator`: проверяет DWG, готовит фоновую базу и вставляет результат в целевой чертеж.
4. `ViewportLayoutExporter` / `LayoutProjectionProcessor`: открывают исходный DWG через `Database(false, true)`, переводят базу в millimeters/metric и переносят первый Paper Space Layout в Model Space.
5. `DimensionStyleNormalizer`: создает viewport-специфичные размерные стили, очищает DSTYLE overrides и сохраняет визуальный масштаб размеров.
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

Сборка автоматически создает и разворачивает bundle в `%AppData%\Autodesk\ApplicationPlugins\AutoBIMFusion.bundle`. `dotnet clean` удаляет развернутый bundle.

### Версии пакетов

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

## Headless diagnostics

```powershell
.\tools\Run-MergeDwgDiagTest.ps1
.\tools\Run-MergeDwgDiagTest.ps1 -Configuration DebugA27 -AutoCADRoot "C:\Program Files\Autodesk\AutoCAD 2027"
.\tools\Run-MergeDwgDiagTest.ps1 -SkipBuild
```

Скрипт строит core bundle с `/p:CoreConsoleDiagnostics=true`, разворачивает его в локальный output и запускает `accoreconsole.exe`. Сценарий сейчас нерабочий: команда `MERGEDWG_DIAG_TEST` не зарегистрирована.

## Документация

- [Техническое описание](AutoBIMFusion/docs/TECHNICAL_DOCUMENTATION.md)
- [Алгоритм слияния](AutoBIMFusion/docs/ALGORITHM.md)
- [Известные проблемы](AutoBIMFusion/docs/KNOWN_ISSUES.md)
