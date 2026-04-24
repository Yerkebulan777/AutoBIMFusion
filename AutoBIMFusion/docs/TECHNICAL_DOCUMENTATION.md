# AutoBIMFusion: Техническая документация

## Содержание
- [1. Обзор системы](#1-обзор-системы)
- [2. Архитектура и поток выполнения](#2-архитектура-и-поток-выполнения)
- [3. Команды и внутренний API](#3-команды-и-внутренний-api)
- [4. Модели данных](#4-модели-данных)
- [5. Требования к окружению](#5-требования-к-окружению)
- [6. Зависимости и сборка](#6-зависимости-и-сборка)
- [7. Развертывание bundle](#7-развертывание-bundle)
- [8. Обработка ошибок и логирование](#8-обработка-ошибок-и-логирование)
- [9. Ограничения текущей реализации](#9-ограничения-текущей-реализации)
- [10. Связанные документы](#10-связанные-документы)

## 1. Обзор системы

`AutoBIMFusion` — AutoCAD-плагин для пакетного объединения DWG-файлов в один итоговый чертёж.

- Тип приложения: .NET plugin (`IExtensionApplication`) для AutoCAD.
- Основные сценарии: команды `MERGEDWG`, `SMART_MERGE_TEXT` и `CreateETransmitZip`.
- Платформа: `net8.0-windows8.0`, x64.
- Поддерживаемые версии AutoCAD: 2025 / 2026 / 2027 (через отдельные build-конфигурации).

## 2. Архитектура и поток выполнения

### 2.1 Модули

| Модуль | Назначение | Ключевые элементы |
| :--- | :--- | :--- |
| `Application/AutoBIMFusionExtension.cs` | Инициализация плагина и Ribbon | `AutoBIMFusionExtension.Initialize`, `OnIdle`, `RibbonBuilder.CreateTab` |
| `Application/Commands` | AutoCAD-команды | `MergeCommands`, `AdvancedTextCommands`, `TransmittalCommands` |
| `Application/Merge` | Оркестрация merge и вставки | `DwgMerger` (статика), `BlockInserter`, `RasterImagePathFixer`, `MergeResult`, `MergeStatistics` |
| `Application/Merge/Layouts` | Экспорт листа с учетом viewport | `ViewportLayoutExporter` (статика), `ViewportCollector`, `ViewportTransformer`, `ModelSpaceTrimmer`, `LayoutViewportInfo` |
| `Application/Utils` | Выбор папки, перечисление и валидация файлов | `FolderSelector`, `FileEnumerator`, `FileHelper`, `LayoutUtil` |
| `Application/Ribbon` | UI-интеграция в ленту AutoCAD | `RibbonBuilder`, `ButtonCommandHandler`, `RibbonIconLoader` |
| `Infrastructure/Logging` | Логирование в Editor и файл | `OperationLogger`, `LoggerFactory` |

### 2.2 Фактический runtime-поток

1. Плагин загружается, на `App.Idle` создаётся вкладка Ribbon и пишется сообщение о загрузке.
2. Пользователь запускает `MERGEDWG`.
3. `MergeCommands`:
   - блокирует параллельный запуск через `SemaphoreSlim _mergeGate`,
   - выбирает папку,
   - получает список DWG через `FileEnumerator.GetFiles`.
4. Внутри `doc.LockDocument()`:
   - вычисляется `savePath` (`<ParentFolder>\<SelectedFolder>.dwg`),
   - создается экземпляр `BlockInserter`.
   - по каждому файлу вызывается статический `DwgMerger.MergeSingleFile`.
5. `DwgMerger`:
   - валидирует файл (`FileHelper`),
   - экспортирует первый layout в temp DWG через статический `ViewportLayoutExporter.ExportToTempAsync`,
   - читает границы temp DWG,
   - вставляет нативные объекты в target DB через переданный `BlockInserter`.
6. После цикла:
   - `RasterImagePathFixer.CopyImagesToTargetFolder` копирует и перепривязывает растр,
   - выполняется `SaveAs(..., AC1032)`,
   - отправляются команды `REGENALL` и `ZOOM EXTENTS`,
   - показывается сводка с метриками.

Отдельная команда `SMART_MERGE_TEXT` работает локально в текущем чертеже: собирает `DBText` из Model Space, группирует по стилю/высоте и геометрической близости, затем заменяет группы на `MText`.

Команда `CreateETransmitZip` формирует eTransmit-пакет текущего сохраненного DWG и архивирует собранные зависимости в ZIP-файл в папке `ETransmitOutput` рядом с чертежом.

## 3. Команды и внутренний API

### 3.1 Внешняя команда AutoCAD

| Команда | Параметры | Результат |
| :--- | :--- | :--- |
| `MERGEDWG` | Без аргументов, выбор папки через диалог | Создание итогового DWG в родительской директории выбранной папки |
| `SMART_MERGE_TEXT` | Без аргументов | Объединение цепочек `TEXT` в `MText` в Model Space |
| `CreateETransmitZip` | Без аргументов | Формирование eTransmit-пакета и ZIP-архива зависимостей текущего DWG |

### 3.2 Внутренние ключевые методы

| Метод | Назначение |
| :--- | :--- |
| `MergeCommands.MergeDwgFolderCommand()` | Точка входа команды |
| `AdvancedTextCommands.SmartMergeModelText()` | Точка входа команды умного объединения `TEXT` |
| `TransmittalCommands.CreateETransmitZip()` | Точка входа команды упаковки eTransmit в ZIP |
| `DwgMerger.MergeSingleFile(string filePath, BlockInserter inserter, Database targetDb, OperationLogger log)` | Статическая обработка одного DWG |
| `ViewportLayoutExporter.ExportToTempAsync(string sourceFilePath, string fileName, OperationLogger log)` | Статический экспорт первого листа во временный DWG |
| `BlockInserter.InsertNativeObjects(Database targetDb, string sourceFilePath, string sourceName, Extents3d sourceBounds)` | Клонирование и смещение нативных объектов |
| `RasterImagePathFixer.CopyImagesToTargetFolder(Database db, string targetFilePath, OperationLogger log)` | Копирование и нормализация путей растров |

## 4. Модели данных

### 4.1 `MergeResult`

Файл: `Application/Merge/MergeResult.cs`

| Поле | Тип | Описание |
| :--- | :--- | :--- |
| `Success` | `bool` | Файл обработан успешно |
| `FileName` | `string` | Имя исходного файла |
| `BlockName` | `string?` | Логическое имя результата обработки файла |
| `IsSkipped` | `bool` | Файл пропущен без критической ошибки |
| `Message` | `string?` | Сокращенное сообщение причины |

Фабрики: `Ok`, `Warn`, `Fail`.

### 4.2 `MergeStatistics`

Файл: `Application/Merge/MergeStatistics.cs`

| Поле | Тип | Описание |
| :--- | :--- | :--- |
| `TotalFiles` | `int` | Всего файлов в обработке |
| `Successful` | `int` | Успешно обработано |
| `Skipped` | `int` | Пропущено |
| `Failed` | `int` | Ошибки |

### 4.3 `LayoutViewportInfo`

Файл: `Application/Merge/Layouts/LayoutViewportInfo.cs`

Используется для viewport-aware математики:
- позиция и размер viewport на листе,
- параметры вида модели,
- window model-space,
- метрика выбора главного viewport.

## 5. Требования к окружению

- ОС: Windows x64.
- .NET SDK: 8.x.
- Runtime target: `net8.0-windows8.0`.
- AutoCAD: 2025 / 2026 / 2027.

## 6. Зависимости и сборка

Версии и конфигурации берутся из централизованных файлов:
- `Directory.Build.props`
- `Directory.Packages.props`

### 6.1 Основные пакеты

| Пакет | Версия |
| :--- | :--- |
| `Serilog` | `4.0.0` |
| `Serilog.Sinks.File` | `6.0.0` |
| `AutoCAD.NET` | `$(AcadPackageVersion).*` |
| `AutoCAD.NET.Interop` | `$(AcadInteropPackageVersion).*` |

### 6.2 Поддерживаемые конфигурации сборки

- `DebugA25`, `ReleaseA25`
- `DebugA26`, `ReleaseA26`
- `DebugA27`, `ReleaseA27`

### 6.3 Команды

```powershell
dotnet restore
dotnet build AutoBIMFusion.slnx -c ReleaseA25
```

(Аналогично для `ReleaseA26` и `ReleaseA27`.)

## 7. Развертывание bundle

Развертывание выполняется таргетом `CreateAutoCADBundle` в `AutoBIMFusion.csproj`.

Основное:
- формируется `PackageContents.xml`,
- копируются DLL/зависимости/иконки,
- bundle разворачивается в:
  - `%AppData%\Autodesk\ApplicationPlugins\` (по умолчанию),
  - либо `%ProgramData%\Autodesk\ApplicationPlugins\`, если `DeployForAllUsers=true`.

## 8. Обработка ошибок и логирование

### 8.1 Обработка ошибок

- Ошибки файла инкапсулируются в `MergeResult`.
- Критические исключения в командах перехватываются в `MergeCommands`, `AdvancedTextCommands` и `TransmittalCommands`.
- Временные файлы чистятся в `finally` (`DwgMerger`).
- Для операций сохранения применяется `AcadWarningSuppressScope`.

### 8.2 Логирование

- `OperationLogger` пишет в:
  - командную строку AutoCAD (`Editor.WriteMessage`),
  - файл Serilog.
- Файл лога: `Logs/merge-yyyy-MM-dd.log` рядом с assembly.
- Дополнительно используется `DiagnosticSink` (Debug/Trace).

## 9. Ограничения текущей реализации

- Обрабатывается только первый найденный layout в каждом исходном DWG.
- Глубина рекурсивного поиска файлов ограничена `3`.
- Файлы больше `15 МБ` исключаются на этапе перечисления.
- Итоговый формат сохранения фиксирован: `DwgVersion.AC1032`.
- Путь итогового файла: родительская директория выбранной папки + имя папки (`<Folder>.dwg`).
- Конвертация растров (`RasterImage` → `OLE2FRAME`) выполняется через системный Clipboard и команду `PASTECLIP`, что делает её неатомарной и зависимой от внешних процессов.
- Поиск вновь созданного `OLE2FRAME` после `PASTECLIP` осуществляется эвристически по наибольшему Handle — не гарантируется API и может давать сбои.
- `Clipboard.Clear()` после встраивания растров безвозвратно удаляет текущее содержимое буфера обмена пользователя.
- Большие изображения через OLE вставляются крайне нестабильно и медленно. Рекомендуется внедрить порог по размеру (например, 5 МБ или 2500×2500 px): превышение порога — оставлять `RasterImage` без конвертации.

## 10. Связанные документы

- Алгоритм: `AutoBIMFusion/docs/ALGORITHM.md`
- Инструкции репозитория: `.github/copilot-instructions.md`

---

## Актуальность

Документ синхронизирован с текущим кодом на дату: **2026-04-24**.

