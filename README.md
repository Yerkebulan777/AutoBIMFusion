# AutoBIMFusion

AutoBIMFusion — плагин AutoCAD .NET для AutoCAD 2025-2027. Основная команда `MERGEDWG` объединяет DWG-файлы из выбранной папки в активный чертеж, сохраняя импортированную геометрию как редактируемые нативные объекты Model Space.

Фактический проект `AutoBIMFusion/AutoBIMFusion.csproj` таргетит `net8.0` и `x64`. Общий `Directory.Build.props` содержит `net10.0-windows`, но проект переопределяет TargetFramework на `net8.0`.

## Команды

| Команда | Назначение | Ribbon |
| :--- | :--- | :--- |
| `MERGEDWG` | Рекурсивно находит DWG, экспортирует первый Layout каждого файла в Model Space и вставляет результат в текущий чертеж. | Да |

Команды `SMART_MERGE_TEXT`, `MERGE_TEXT_STYLES`, `JOIN_LINES` и `CREATE_ETRANSMIT_ZIP` временно перемещены в `Application/Commands/Archive` и исключены из сборки, поэтому AutoCAD их не регистрирует.

`tools/Run-MergeDwgDiagTest.ps1` сейчас запускает `MERGEDWG_DIAG_TEST`, но такой `[CommandMethod]` отсутствует в текущем коде. Поэтому headless diagnostic documented as known issue, а не как рабочий acceptance test.

## Процесс слияния

1. **`CombineCommands`**: точка входа `MERGEDWG`, выбор папки, защита от параллельного запуска через `SemaphoreSlim`, прогресс и финальное сохранение.
2. **`FileUtil`**: собирает `.dwg` рекурсивно до 3 уровней, пропускает файлы с префиксом `#` и файлы больше 15 МБ, сортирует естественным порядком.
3. **`CombineOrchestrator`**: проверяет каждый DWG, готовит фоновые базы и вызывает вставку.
4. **`ViewportLayoutExporter` / `LayoutProjectionProcessor`**: открывает исходный DWG в `Database(false, true)`, без временного DWG-файла, переносит выбранный Paper Space Layout в Model Space.
5. **`DimensionStyleNormalizer`**: нормализует размерные стили до `WblockCloneObjects`, очищает DSTYLE overrides и сохраняет визуальный масштаб размеров.
6. **`BlockInserter`**: клонирует нативные объекты через `WblockCloneObjects` и раскладывает листы вдоль оси X с зазором 10%.
7. **Финализация**: `RasterImagePathFixer`, диагностический снимок стилей, `DwgOptimizer`, сохранение в `DwgVersion.AC1032`, затем `REGENALL` и `ZOOM EXTENTS`.

Логи всех активных команд пишутся в `%AppData%\Autodesk\ApplicationPlugins\AutoBIMFusion.bundle\Contents\Logs\merge-YYYY-MM-DD.log`.

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

NuGet versions централизованы в `Directory.Packages.props`: `AutoCAD.NET` использует `$(AcadPackageVersion).*`, `AutoCAD.NET.Interop` использует `$(AcadInteropPackageVersion).*`, Serilog зафиксирован на `4.0.0`, `Serilog.Sinks.File` на `6.0.0`.

## Headless diagnostics

```powershell
.\tools\Run-MergeDwgDiagTest.ps1
.\tools\Run-MergeDwgDiagTest.ps1 -Configuration DebugA27 -AutoCADRoot "C:\Program Files\Autodesk\AutoCAD 2027"
.\tools\Run-MergeDwgDiagTest.ps1 -SkipBuild
```

Скрипт строит core bundle с `/p:CoreConsoleDiagnostics=true`, разворачивает его в локальный output и запускает `accoreconsole.exe`. На текущем коде сценарий заблокирован, потому что команда `MERGEDWG_DIAG_TEST` не зарегистрирована.

## Документация

- [Техническое описание](AutoBIMFusion/docs/TECHNICAL_DOCUMENTATION.md)
- [Алгоритм слияния](AutoBIMFusion/docs/ALGORITHM.md)
- [Известные проблемы](AutoBIMFusion/docs/KNOWN_ISSUES.md)
