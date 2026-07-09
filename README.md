# AutoBIMFusion

Плагин AutoCAD .NET (2025–2027). Команда `MERGEDWG` рекурсивно находит DWG в выбранной папке, экспортирует первый Layout каждого файла в Model Space и вставляет результат в текущий чертеж как нативные объекты AutoCAD.

## Пакетная обработка директории

Скрипт `tools/Start-MergeDwgBatch.ps1` запускает отдельный процесс AutoCAD для каждой папки с исходными DWG и автоматически сохраняет результат.

**Пример структуры папок:**

```
D:\DWG-Batch\
  Object-01\      ← содержит a.dwg, b.dwg
  Object-02\      ← содержит source.dwg
```

**Запуск:**

```powershell
cd D:\DWG-Batch
powershell -ExecutionPolicy Bypass -File "C:\Users\y.zhumabayev\Repository\AutoBIMFusion\tools\Start-MergeDwgBatch.ps1"
```

Скрипт сам собирает плагин (`dotnet build`), запускает AutoCAD для каждой папки и создаёт рядом папку с суффиксом `-сборка` с итоговым DWG.

**Параметры:**

| Параметр | Описание |
|---|---|
| `-WhatIf` | Показать, какие папки будут обработаны, без запуска AutoCAD |
| `-SkipBuild` | Пропустить сборку (если плагин уже собран) |
| `-MaxParallel 2` | Максимум параллельных процессов AutoCAD |
| `-StartDelaySeconds 10` | Задержка между стартами процессов |
| `-AutoCADRoot "C:\Program Files\Autodesk\AutoCAD 2027"` | Путь к AutoCAD |
| `-Configuration DebugA27` | Конфигурация сборки |
| `-TimeoutMinutes 120` | Таймаут на один процесс |

## Сборка

```powershell
dotnet build AutoBIMFusion.slnx -c DebugA26
```

Конфигурации: `DebugA25`, `DebugA26`, `DebugA27`, `ReleaseA25`, `ReleaseA26`, `ReleaseA27`.

Плагин автоустанавливается в `%AppData%\Autodesk\ApplicationPlugins\AutoBIMFusion.bundle`.

Для ручной установки:

```powershell
powershell -ExecutionPolicy Bypass -File tools\Install-AutoBIMFusionBundle.ps1
```

## Команды

| Команда | Описание |
|---|---|
| `MERGEDWG` | Выбрать папку → слияние всех DWG в текущий чертёж |
| `MERGEDWG_BATCH` | Внутренняя команда для пакетного запуска (не вызывать вручную) |

## Документация

- [Техническое описание](docs/TECHNICAL_DOCUMENTATION.md)
- [Алгоритм слияния](docs/ALGORITHM.md)
- [Структура проекта](docs/PROJECT_STRUCTURE.md)
- [Известные проблемы](docs/KNOWN_ISSUES.md)

## Логи

Логи слияния: `%LOCALAPPDATA%\AutoBIMFusion\Logs\merge-YYYY-MM-DD.log`.

## Лицензия

См. [LICENSE.txt](LICENSE.txt).
