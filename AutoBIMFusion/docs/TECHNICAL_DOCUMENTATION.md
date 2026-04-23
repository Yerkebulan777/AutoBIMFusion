# AutoBIMFusion: Техническая документация

## Содержание
- [1. Обзор системы](#1-обзор-системы)
- [2. Архитектура и поток выполнения](#2-архитектура-и-поток-выполнения)
- [3. Команды и публичный API плагина](#3-команды-и-публичный-api-плагина)
- [4. Модели данных (вместо схем БД)](#4-модели-данных-вместо-схем-бд)
- [5. Требования к окружению](#5-требования-к-окружению)
- [6. Зависимости и установка](#6-зависимости-и-установка)
- [7. Развертывание и конфигурация сред](#7-развертывание-и-конфигурация-сред)
- [8. Обработка ошибок и логирование](#8-обработка-ошибок-и-логирование)
- [9. Примеры использования ключевых функций](#9-примеры-использования-ключевых-функций)
- [10. Контрибьютинг для разработчиков](#10-контрибьютинг-для-разработчиков)
- [11. Известные проблемы и планируемые улучшения](#11-известные-проблемы-и-планируемые-улучшения)
- [12. Актуальность ссылок и версий](#12-актуальность-ссылок-и-версий)

## 1. Обзор системы

`AutoBIMFusion` — плагин AutoCAD для пакетного объединения DWG-файлов в один итоговый чертеж.

- Формат исполнения: .NET-плагин для AutoCAD (`IExtensionApplication`).
- Основной сценарий: команда `MERGEDWG` выбирает папку, обрабатывает DWG-файлы, сохраняет объединенный результат.
- Поддерживаемые платформы: AutoCAD 2025/2026/2027 (Win64), .NET `net8.0-windows8.0`.
- Тип API проекта: командный API AutoCAD и внутренние C# сервисы (HTTP API отсутствует).

## 2. Архитектура и поток выполнения

### 2.1 Слои и модули

| Модуль | Назначение | Ключевые элементы |
| :--- | :--- | :--- |
| `Application/AutoBIMFusionExtension.cs` | Точка инициализации плагина | `AutoBIMFusionExtension.Initialize`, создание ribbon |
| `Application/Commands` | Входные команды AutoCAD | `MergeCommands.MergeDwgFolderCommand` |
| `Application/Merge` | Оркестрация слияния и сохранения | `DwgMerger`, `BlockInserter`, `MergeSaver` |
| `Application/Merge/Layouts` | Viewport-aware экспорт листов | `ViewportLayoutExporter` и вспомогательные классы |
| `Application/Utils` | Поиск файлов, валидация, выбор папки и служебные утилиты | `FileEnumerator`, `FileHelper`, `FolderSelector`, `LayoutUtil` |
| `Application/Ribbon` | UI-интеграция в ленту AutoCAD | `RibbonBuilder`, `ButtonCommandHandler` |
| `Infrastructure/Logging` | Единый логгер в файл + вывод в Editor | `OperationLogger`, `LoggerFactory` |

### 2.2 Поток выполнения (актуальная схема)

1. Инициализация плагина через `AutoBIMFusionExtension`.
2. Пользователь запускает команду `MERGEDWG`.
3. `MergeCommands`:
   - предотвращает повторный запуск через `_isProcessing`,
   - выбирает каталог,
   - собирает набор файлов через `FileEnumerator`.
4. `DwgMerger` для каждого файла:
   - валидация файла (`FileHelper`),
   - экспорт первого листа через `ViewportLayoutExporter`,
   - вычисление `Extents3d`,
   - вставка через `BlockInserter` (`AttachXref` + `BindXrefs`).
5. `MergeSaver` сохраняет итоговый DWG (`AC1032`).
6. `MergeCommands` выполняет `REGENALL` и `ZOOM EXTENTS`, показывает сводку.

## 3. Команды и публичный API плагина

Проект не публикует HTTP/REST endpoints. Внешним API являются команды AutoCAD и точки расширения плагина.

### 3.1 Команда AutoCAD

| Идентификатор | Параметры | Возвращаемый результат | Где определено |
| :--- | :--- | :--- | :--- |
| `MERGEDWG` | Без аргументов в командной строке. Папка выбирается через диалог | Создает `<ИмяПапки>.dwg` в родительской директории исходной папки | `Application/Commands/MergeCommands.cs` |

### 3.2 Внутренний сервисный API (для разработчиков)

| Метод | Параметры | Назначение |
| :--- | :--- | :--- |
| `DwgMerger.Init(Database targetDb)` | `targetDb`: целевая БД чертежа AutoCAD | Инициализация состояния перед циклом слияния |
| `DwgMerger.MergeSingleFile(string filePath, Database targetDb)` | `filePath`: путь к DWG, `targetDb`: целевая БД | Обработка одного файла, возврат `MergeResult` |
| `BlockInserter.BuildUniqueName(string baseName)` | `baseName`: базовое имя блока | Генерация уникального имени блока |
| `BlockInserter.InsertAndBindXref(Database targetDb, string sourceFilePath, string blockName, Extents3d sourceBounds)` | Целевая БД, путь к временному файлу, имя блока, границы исходника | Вставка Xref + Bind, возврат мировых границ вставки |
| `MergeSaver.SaveMerged(Database db, string savePath, OperationLogger log)` | БД, путь сохранения, логгер | Сохранение итогового DWG |

## 4. Модели данных (вместо схем БД)

В проекте отсутствует СУБД и миграции, поэтому раздел «схемы БД» заменен на модели данных домена.

### 4.1 `MergeResult`

Файл: `Application/Merge/MergeResult.cs`

| Поле | Тип | Описание |
| :--- | :--- | :--- |
| `Success` | `bool` | Успешность обработки файла |
| `FileName` | `string` | Имя исходного файла |
| `BlockName` | `string?` | Имя вставленного блока (если есть) |
| `IsSkipped` | `bool` | Пропуск файла без критической ошибки |
| `Message` | `string?` | Краткое пояснение результата |

Фабричные методы: `Ok`, `Warn`, `Fail`.

### 4.2 `MergeStatistics`

Файл: `Application/Merge/MergeStatistics.cs`

| Поле | Тип | Описание |
| :--- | :--- | :--- |
| `TotalFiles` | `int` | Всего файлов в цикле |
| `Successful` | `int` | Успешно обработано |
| `Skipped` | `int` | Пропущено |
| `Failed` | `int` | Ошибки |

### 4.3 `LayoutViewportInfo`

Файл: `Application/Merge/Layouts/LayoutViewportInfo.cs`

Ключевые атрибуты модели viewport:
- геометрия на листе (`CenterPaper`, `WidthPaper`, `HeightPaper`),
- параметры просмотра (`ViewCenter`, `ViewHeight`, `ViewTwist`, `CustomScale`),
- окно модели (`ModelWindow`),
- расчетный показатель покрытия (`CoverageScore`).

## 5. Требования к окружению

### 5.1 Минимальные требования
- ОС: Windows 8.0+ x64.
- AutoCAD: 2025 / 2026 / 2027 (64-bit).
- .NET runtime: `.NET 8 Desktop Runtime`.
- SDK для сборки: `.NET SDK 8.x`.

### 5.2 Инструменты разработки
- Visual Studio 2022 (рекомендуется) с workload .NET desktop development.
- Доступ к NuGet для восстановления пакетов.

## 6. Зависимости и установка

### 6.1 Основные NuGet-зависимости

Версии централизованы в `Directory.Packages.props`.

| Пакет | Версия |
| :--- | :--- |
| `Serilog` | `4.0.0` |
| `Serilog.Sinks.File` | `6.0.0` |
| `AutoCAD.NET` | `$(AcadPackageVersion).*` (зависит от конфигурации) |
| `AutoCAD.NET.Interop` | `$(AcadInteropPackageVersion).*` (зависит от конфигурации) |

### 6.2 Установка зависимостей и сборка

```powershell
dotnet restore
dotnet build AutoBIMFusion.slnx -c ReleaseA26
```

Поддерживаемые конфигурации: `DebugA25`, `DebugA26`, `DebugA27`, `ReleaseA25`, `ReleaseA26`, `ReleaseA27`.

## 7. Развертывание и конфигурация сред

Развертывание управляется таргетом `CreateAutoCADBundle` в `AutoBIMFusion/AutoBIMFusion.csproj`.

### 7.1 Development
- Типичная конфигурация: `DebugA25/26/27`.
- Развертывание в профиль текущего пользователя:
  - `%AppData%\Autodesk\ApplicationPlugins\AutoBIMFusion.bundle`
- Флаг: `DeployForAllUsers=false` (по умолчанию).

### 7.2 Testing
- Рекомендуемая конфигурация: `ReleaseA25/26/27`.
- Установка в user plugins directory для изолированного тестирования.
- Проверка сценариев:
  - обработка пустых каталогов,
  - обработка файлов > 15 МБ,
  - ветки `0 VP`, `1 VP`, `2+ VP`.

### 7.3 Production
- Сборка `ReleaseA25/26/27` для целевой версии AutoCAD.
- Для установки всем пользователям требуется:
  - `DeployForAllUsers=true`,
  - запуск сборки с правами администратора,
  - размещение в `%ProgramData%\Autodesk\ApplicationPlugins\AutoBIMFusion.bundle`.

## 8. Обработка ошибок и логирование

### 8.1 Обработка ошибок
- Бизнес-результат обработки файла передается через `MergeResult` (`Ok/Warn/Fail`).
- Критические ошибки логируются и пробрасываются/обрабатываются в `MergeCommands`.
- Валидация входных данных выполняется через `ArgumentNullException.ThrowIfNull` и служебные проверки `FileHelper`.
- Временные документы закрываются в `finally`, временные файлы удаляются после обработки.
- Для OLE-встраивания используется двухступенчатая коррекция размеров (`WcsWidth/WcsHeight` + fallback через `Position3d`), чтобы исключить аномально большие `OLE2FRAME` в отдельных DWG.

### 8.2 Логирование
- `OperationLogger` пишет:
  - в командную строку AutoCAD (`Editor.WriteMessage`),
  - в файловый лог через `Serilog`.
- Файловый лог: `Logs/merge-yyyy-MM-dd.log` рядом с собранной DLL.
- Дополнительный sink: `DiagnosticSink` (Debug/Trace).
- Для suppress предупреждений при сохранении/экспорте используется `AcadWarningSuppressScope`.
- Сообщения вида `TransformBy(Displacement) не сработал для Ole2Frame. Пробуем Position3d.` относятся к fallback-пути и сами по себе не означают провал merge-операции.

## 9. Примеры использования ключевых функций

### 9.1 Запуск слияния из команды

```csharp
[CommandMethod("MERGEDWG", CommandFlags.Session)]
public async void MergeDwgFolderCommand()
{
    Document doc = AcadApp.DocumentManager.MdiActiveDocument!;
    using OperationLogger log = new(doc.Editor);

    using (doc.LockDocument())
    {
        using DwgMerger merger = new(gapPercent: 0.1, log);
        merger.Init(doc.Database);

        MergeResult result = await merger.MergeSingleFile(@"C:\\dwg\\sheet1.dwg", doc.Database);
        log.Info($"Result: {(result.Success ? "OK" : "FAIL")}");
    }
}
```

### 9.2 Сохранение объединенного файла

```csharp
string savePath = @"C:\\output\\Merged.dwg";
MergeSaver.SaveMerged(doc.Database, savePath, log);
```

### 9.3 Вставка и bind временного DWG вручную

```csharp
BlockInserter inserter = new(0.1, log);
inserter.Init(targetDb);

string blockName = inserter.BuildUniqueName("Sheet_A1");
Extents3d srcBounds = new(new Point3d(0, 0, 0), new Point3d(1000, 700, 0));
Extents3d? world = inserter.InsertAndBindXref(targetDb, tempPath, blockName, srcBounds);
```

## 10. Контрибьютинг для разработчиков

### 10.1 Базовые правила
- Соблюдать текущую архитектуру без избыточных абстракций (flatter architecture).
- Использовать явные проверки аргументов (`ThrowIfNull`).
- Не добавлять local functions без необходимости.
- Поддерживать детерминированное логирование и короткие сообщения об ошибках.

### 10.2 Процесс внесения изменений
1. Создать ветку от актуального `main`.
2. Выполнить изменения с сохранением существующих паттернов проекта.
3. Проверить сборку минимум для одной целевой конфигурации (`ReleaseA26` или требуемой).
4. Обновить документацию при изменении поведения алгоритма или требований.
5. Оформить PR с описанием изменения, рисков и шагов верификации.

### 10.3 Что обязательно документировать
- Изменения в алгоритме экспорта viewport.
- Изменения в ограничениях (размер файлов, глубина рекурсии, версия DWG).
- Новые команды AutoCAD и параметры конфигурации MSBuild.

## 11. Известные проблемы и планируемые улучшения

### 11.1 Известные проблемы
- Обрабатывается только первый лист (`TabOrder`) каждого DWG.
- Файлы более 15 МБ исключаются на этапе перечисления (`FileEnumerator`).
- При отсутствии листа используется fallback-обработка исходного файла.
- Сохранение итогового файла фиксировано в `AC1032`.
- В отдельных CAD-сценариях геометрические трансформации OLE частично игнорируются ядром AutoCAD; применяется fallback через `Position3d`, но визуальную проверку проблемных листов рекомендуется оставлять в регрессионном чек-листе.

### 11.2 Планируемые улучшения
- Поддержка выбора нескольких листов из одного исходного DWG.
- Параметризация лимита размера файла и глубины обхода директорий.
- Гибкая настройка формата сохранения DWG в зависимости от целевой версии.
- Расширенная диагностическая телеметрия по каждому этапу merge-пайплайна.

## 12. Актуальность ссылок и версий

Актуальность проверена на основе текущих файлов проекта:
- `Directory.Build.props`: таргет `net8.0-windows8.0`, конфигурации для AutoCAD 2025/2026/2027.
- `Directory.Packages.props`: версии `Serilog 4.0.0`, `Serilog.Sinks.File 6.0.0`.
- `AutoBIMFusion/AutoBIMFusion.csproj`: автоматическая сборка и публикация `ApplicationPlugins` bundle.
- `README.md` обновлен ссылкой на этот документ.

Связанные документы:
- Алгоритм: `AutoBIMFusion/docs/ALGORITHM.md`
- Корневой обзор: `README.md`
