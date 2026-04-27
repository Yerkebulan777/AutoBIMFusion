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

`AutoBIMFusion` — высокопроизводительный AutoCAD-плагин для пакетного объединения DWG-файлов в один итоговый чертёж с поддержкой смарт-объединения текста, нормализации стилей и создания eTransmit-пакетов.

- **Тип приложения:** .NET 8 plugin (`IExtensionApplication`) для AutoCAD.
- **Основные команды:**
  - `MERGEDWG` — пакетное объединение DWG-файлов из выбранной папки
  - `SMART_MERGE_TEXT` — интеллектуальное объединение цепочек текстовых объектов в MText
  - `MergeTextStyles` — нормализация и объединение дубликатов текстовых стилей
  - `CreateETransmitZip` — формирование eTransmit-пакета в ZIP-архив
- **Платформа:** `net8.0-windows8.0`, x64.
- **Поддерживаемые версии AutoCAD:** 2025 / 2026 / 2027 (через отдельные build-конфигурации).

### Ключевые требования MERGEDWG

1. **Нативная геометрия:** Объекты в итоговом чертеже должны быть в том же виде, как и в исходном файле — не обёрнуты в блок. При слиянии DWG-файлов содержимое каждого исходного файла вставляется напрямую в Model Space целевого документа. Это принципиальное архитектурное решение для сохранения редактируемости.
2. **Максимальная точность (Mandate):** Вычисление границ геометрии (`Extents`), коэффициентов масштабирования и матриц трансформации должно выполняться с максимально возможной точностью. 
   - **Точность вычислений является приоритетом №1**, даже если это идет в ущерб скорости обработки. 
   - Не допускаются никакие оптимизации, которые могут привести к погрешности в позиционировании или определении габаритов объектов.

## 2. Архитектура и поток выполнения

### 2.1 Структура проекта

```
AutoBIMFusion/
├── Application/
│   ├── AutoBIMFusionExtension.cs      # Точка входа плагина
│   ├── AcadSupport/
│   │   └── AcadWarningSuppressScope.cs # Подавление предупреждений AutoCAD
│   ├── Commands/                       # Внешние команды AutoCAD
│   │   ├── MergeCommands.cs
│   │   ├── AdvancedTextCommands.cs
│   │   ├── TextStyleCommands.cs
│   │   └── TransmittalCommands.cs
│   ├── Merge/                          # Ядро слияния и обработки
│   │   ├── DwgMerger.cs                # Статическая обработка файлов
│   │   ├── BlockInserter.cs            # Вставка объектов в целевой документ
│   │   ├── RasterImagePathFixer.cs     # Обработка растровых изображений
│   │   ├── MergeResult.cs              # Результат обработки файла
│   │   ├── MergeStatistics.cs          # Статистика операции
│   │   └── Layouts/                    # Layout-aware экспорт и трансформация
│   │       ├── ViewportLayoutExporter.cs
│   │       ├── ViewportCollector.cs
│   │       ├── ViewportTransformer.cs
│   │       ├── ModelSpaceTrimmer.cs
│   │       ├── DrawOrderPreserver.cs
│   │       ├── ExtentsUtils.cs
│   │       └── LayoutViewportInfo.cs
│   ├── Ribbon/                         # UI-интеграция в ленту AutoCAD
│   │   ├── RibbonBuilder.cs
│   │   ├── ButtonCommandHandler.cs
│   │   └── RibbonIconLoader.cs
│   └── Utils/                          # Утилиты
│       ├── FileEnumerator.cs           # Перечисление и фильтрация файлов
│       ├── FileHelper.cs               # Валидация и проверка файлов
│       ├── FolderSelector.cs           # Диалог выбора папки
│       ├── LayoutUtil.cs               # Утилиты работы с layout'ами
│       └── WindowsNaturalComparer.cs   # Натуральная сортировка файлов
└── Infrastructure/
    └── Logging/
        ├── OperationLogger.cs          # Логирование в Editor и файл
        └── LoggerFactory.cs            # Инициализация Serilog
```

### 2.2 Инициализация плагина

При загрузке AutoCAD:

1. **`AutoBIMFusionExtension.Initialize()`** вызывает подписку на `App.Idle`.
2. На первом `Idle`:
   - создается вкладка Ribbon через `RibbonBuilder.CreateTab()`,
   - записывается сообщение о загрузке в логи и командную строку,
   - обработчик Idle отписывается от события.
3. **`AutoBIMFusionExtension.Terminate()`** вызывает отписку при выгрузке (если она произойдет).

### 2.3 Runtime-поток выполнения команды MERGEDWG

```
Пользователь → MERGEDWG
  ↓
MergeCommands.MergeDwgFolderCommand()
  ↓
MergeDwgFolderCommandAsync()
  ├─ Проверка SemaphoreSlim (защита от параллельного запуска)
  ├─ Создание OperationLogger
  ├─ ExecuteMerge(doc, log)
  │   ├─ FolderSelector.SelectFolder()              [диалог выбора папки]
  │   ├─ FileEnumerator.GetFiles(selectedPath)      [получение списка DWG]
  │   ├─ Вычисление savePath (родитель + имя папки)
  │   ├─ Создание BlockInserter(targetDb, savePath)
  │   ├─ doc.LockDocument()
  │   │   ├─ foreach file in files:
  │   │   │   └─ DwgMerger.MergeSingleFile(...)     [обработка одного файла]
  │   │   │       ├─ FileHelper.ValidateDwgFile()
  │   │   │       ├─ ViewportLayoutExporter.ExportToTempAsync()
  │   │   │       ├─ BlockInserter.InsertNativeObjects()
  │   │   │       └─ ViewportLayoutExporter.EmbedSingleRasterAsync() [если нужно]
  │   │   ├─ RasterImagePathFixer.CopyImagesToTargetFolder()
  │   │   ├─ doc.SaveAs(..., DwgVersion.AC1032)
  │   │   └─ Отправка REGENALL и ZOOM EXTENTS
  │   └─ Вывод статистики
  └─ Обработка ошибок и очистка ресурсов
```

### 2.4 Фактический процесс слияния одного файла

**`DwgMerger.MergeSingleFile()`:**

1. Валидирует файл (размер, расширение, доступность).
2. Экспортирует первый найденный layout в **временный DWG** через `ViewportLayoutExporter.ExportToTempAsync()`:
   - Открывает исходный файл в отдельной базе данных.
   - Считывает bounds первого layout'а.
   - Копирует/экспортирует geometry с учетом viewport'ов (если они есть).
   - Встраивает растровые изображения через `EmbedSingleRasterAsync()` (OLE2FRAME через Clipboard).
   - Закрывает и удаляет временный файл.
3. Вставляет нативные объекты из временного файла в целевой документ через `BlockInserter.InsertNativeObjects()`:
   - Использует `WblockCloneObjects` с `DuplicateRecordCloning.Ignore` (не перезаписывает существующие стили и слои).
   - Клонирует объекты напрямую в Model Space целевого чертежа как нативные сущности (не в блоке).
   - Применяет смещение (displacement) к каждому клонированному объекту индивидуально.
   - **Ключевое требование:** объекты должны быть в том же виде, как в исходном файле (не обёрнуты в блок).

### 2.5 Runtime-поток команд SMART_MERGE_TEXT, MergeTextStyles, CreateETransmitZip

**SMART_MERGE_TEXT:**
Команда выполняет интеллектуальное объединение разрозненных однострочных и многострочных текстов в логические абзацы (единый объект MText).
- **Сбор данных:** Извлекаются все объекты `TEXT` и `MTEXT` из Model Space.
- **Предварительная фильтрация:** Тексты группируются по текстовому стилю и углу поворота. Высота не фильтруется строго (допускается погрешность до 15% между объединяемыми кусками).
- **Поиск соседей (Кластеризация):** 
  - Объекты сортируются сверху вниз по линии, перпендикулярной тексту.
  - Поиск объединяемых соседей идет по двум осям:
    - **По вертикали (LineHeightFactor = 2.0):** проверяется, что соседний текст находится на той же или соседней строке.
    - **По горизонтали (WordWidthFactor = 1.75):** горизонтальный зазор (gap) между текстами не должен превышать 1.75 от ширины объединяемого текста (или 2.5 от высоты шрифта для ультракоротких предлогов). Габариты текста вычисляются точно с учетом выравнивания (AttachmentPoint) для MTEXT и Bounds для TEXT.
- **Объединение (CombineGroupText):** Найденный кластер (абзац) разбивается на строки (учитывая порог 0.5 высоты шрифта). Слова в одной строке склеиваются пробелом, а сами строки объединяются спецсимволом абзаца AutoCAD — `\P`. Если исходные MTEXT имели `\n`, они корректно транслируются в `\P`.
- **Замена геометрии:** Создается новый MText, а исходные объекты удаляются.

**MergeTextStyles:**
- Итерирует таблицу текстовых стилей.
- Находит дубликаты (идентичные параметры: шрифт, наклон, масштаб и т.д.).
- Переназначает все ссылки (`DBText`, `MText`, `AttributeDefinition`, `AttributeReference`) на "мастер-стиль" (первый найденный дубликат).
- Удаляет оставшиеся дубликаты.

**CreateETransmitZip:**
- Использует AutoCAD eTransmit API для сбора зависимостей текущего чертежа (ссылки, растры, шрифты и т.д.).
- Упаковывает результат в ZIP-архив с именем `<DocName>_Package.zip`.
- Сохраняет в папку `ETransmitOutput` рядом с документом.

## 3. Команды и внутренний API

### 3.1 Внешние команды AutoCAD

| Команда | Сигнатура | Назначение |
| :--- | :--- | :--- |
| `MERGEDWG` | `[CommandMethod("MERGEDWG", CommandFlags.Session)]` | Пакетное слияние DWG из папки в один итоговый файл |
| `SMART_MERGE_TEXT` | `[CommandMethod("SMART_MERGE_TEXT")]` | Группировка близких TEXT-объектов в MText по стилю и высоте |
| `MergeTextStyles` | `[CommandMethod("MergeTextStyles")]` | Нормализация дубликатов текстовых стилей и переназначение ссылок |
| `CreateETransmitZip` | `[CommandMethod("CreateETransmitZip")]` | Создание eTransmit-пакета в ZIP-архив |

### 3.2 Ключевые методы внутреннего API

#### MergeCommands

| Метод | Сигнатура | Описание |
| :--- | :--- | :--- |
| `MergeDwgFolderCommand` | `public void` | Точка входа команды `MERGEDWG` |
| `MergeDwgFolderCommandAsync` | `private async Task` | Асинхронная реализация с SemaphoreSlim-блокировкой |
| `ExecuteMerge` | `private async Task` | Ядро операции слияния |

#### DwgMerger (статические методы)

| Метод | Сигнатура | Описание |
| :--- | :--- | :--- |
| `MergeSingleFile` | `public static Task<MergeResult>` | Обработка одного DWG (валидация, экспорт, встраивание) |

#### BlockInserter

| Метод | Сигнатура | Описание |
| :--- | :--- | :--- |
| `InsertNativeObjects` | `public Extents3d?` | Клонирование объектов из временного файла напрямую в Model Space целевого DB как нативных сущностей (не в блоке) с WblockCloneObjects + DuplicateRecordCloning.Ignore |
| `CalcInsertionPoint` | `private Point3d` | Вычисление точки вставки на основе текущих bounds целевого документа и зазора |

#### ViewportLayoutExporter (статические методы)

| Метод | Сигнатура | Описание |
| :--- | :--- | :--- |
| `ExportToTempAsync` | `public static async Task<string>` | Экспорт первого layout'а исходного файла во временный DWG с учетом viewport'ов |
| `EmbedSingleRasterAsync` | `private static async Task` | Встраивание RasterImage через OLE2FRAME (Clipboard + PASTECLIP) |
| `TryCopyImageToClipboard` | `private static bool` | Попытка скопировать изображение в системный Clipboard |
| `FindNewOle2Frame` | `private static ObjectId` | Поиск вновь созданного OLE2FRAME (эвристика по Handle) |

#### RasterImagePathFixer

| Метод | Сигнатура | Описание |
| :--- | :--- | :--- |
| `CopyImagesToTargetFolder` | `public static void` | Копирование и переназначение путей RasterImageDef в целевой папке |

#### FileEnumerator

| Метод | Сигнатура | Описание |
| :--- | :--- | :--- |
| `GetFiles` | `public static List<string>` | Рекурсивное (до MaxRecursionDepth=3) перечисление DWG с фильтром по размеру (< 15 МБ) |

#### FileHelper

| Метод | Сигнатура | Описание |
| :--- | :--- | :--- |
| `ValidateDwgFile` | `public static bool` | Проверка расширения, доступности и размера файла |

#### AdvancedTextCommands

| Метод | Сигнатура | Описание |
| :--- | :--- | :--- |
| `SmartMergeModelText` | `public void` | Точка входа команды SMART_MERGE_TEXT |

#### TextStyleCommands

| Метод | Сигнатура | Описание |
| :--- | :--- | :--- |
| `MergeTextStyles` | `public void` | Точка входа команды MergeTextStyles |

#### TransmittalCommands

| Метод | Сигнатура | Описание |
| :--- | :--- | :--- |
| `CreateETransmitZip` | `public void` | Точка входа команды CreateETransmitZip |

#### OperationLogger

| Метод | Сигнатура | Описание |
| :--- | :--- | :--- |
| `Info` | `public void` | Логирование информационного сообщения |
| `Warn` | `public void` | Логирование предупреждения |
| `Error` | `public void (Exception ex, string message)` | Логирование ошибки с stack trace |

## 4. Модели данных

### 4.1 `MergeResult`

**Файл:** `Application/Merge/MergeResult.cs`

Результат обработки одного файла. Инкапсулирует статус, имя файла, логическое имя блока и описание ошибки.

| Поле | Тип | Назначение |
| :--- | :--- | :--- |
| `Success` | `bool` | Файл обработан без критических ошибок |
| `FileName` | `string` | Имя исходного файла |
| `BlockName` | `string?` | Логическое имя результата (обычно имя временного файла или `null`) |
| `IsSkipped` | `bool` | Файл пропущен (например, слишком большой) |
| `Message` | `string?` | Описание причины (если есть warning или skip) |

**Статические фабрики:**
- `MergeResult.Ok(fileName, blockName)` — успех
- `MergeResult.Warn(fileName, blockName, message)` — успех с warning
- `MergeResult.Fail(fileName, message)` — критическая ошибка

### 4.2 `MergeStatistics`

**Файл:** `Application/Merge/MergeStatistics.cs`

Итоговая статистика операции слияния.

| Поле | Тип | Назначение |
| :--- | :--- | :--- |
| `TotalFiles` | `int` | Всего файлов обнаружено |
| `Successful` | `int` | Успешно обработано |
| `Skipped` | `int` | Пропущено (без ошибок) |
| `Failed` | `int` | Критические ошибки |

### 4.3 `LayoutViewportInfo`

**Файл:** `Application/Merge/Layouts/LayoutViewportInfo.cs`

Информация о viewport'е на layout'е (для viewport-aware трансформации).

| Поле | Тип | Назначение |
| :--- | :--- | :--- |
| `ViewportId` | `ObjectId` | ObjectId viewport'а |
| `Position` | `Point3d` | Позиция на листе (в layout-координатах) |
| `Size` | `Vector2d` | Размер viewport'а на листе |
| `ViewCenter` | `Point2d` | Центр view в модели |
| `ViewWidth` | `double` | Ширина view в модели |
| `ViewHeight` | `double` | Высота view в модели |
| `ViewTwist` | `double` | Угол поворота view |

## 5. Требования к окружению

- **ОС:** Windows x64 (Windows 8.0+).
- **.NET SDK:** 8.x (или выше).
- **Runtime target:** `net8.0-windows8.0`.
- **AutoCAD:** 2025, 2026, 2027 (выбор при сборке).
- **Visual Studio:** 2022+ (рекомендуется Community, Professional или Enterprise).

## 6. Зависимости и сборка

Версии пакетов централизованы в `Directory.Build.props` и `Directory.Packages.props`.

### 6.1 Основные NuGet-зависимости

| Пакет | Назначение |
| :--- | :--- |
| `Serilog` | Логирование структурированного типа |
| `Serilog.Sinks.File` | Вывод логов в файл |
| `AutoCAD.NET` | AutoCAD API (версия зависит от выбранной конфигурации) |
| `AutoCAD.NET.Interop` | COM interop для AutoCAD |

### 6.2 Конфигурации сборки

Поддерживаются отдельные конфигурации для каждой версии AutoCAD:

- `DebugA25` / `ReleaseA25` — AutoCAD 2025
- `DebugA26` / `ReleaseA26` — AutoCAD 2026
- `DebugA27` / `ReleaseA27` — AutoCAD 2027

### 6.3 Команды сборки

```powershell
# Восстановление зависимостей
dotnet restore

# Сборка для AutoCAD 2025
dotnet build AutoBIMFusion.slnx -c ReleaseA25

# Сборка для AutoCAD 2026
dotnet build AutoBIMFusion.slnx -c ReleaseA26

# Сборка для AutoCAD 2027
dotnet build AutoBIMFusion.slnx -c ReleaseA27
```

## 7. Развертывание bundle

Развертывание выполняется через таргет MSBuild `CreateAutoCADBundle` в `AutoBIMFusion.csproj`.

**Процесс:**
1. Генерируется `PackageContents.xml` на основе шаблона.
2. Копируются сборки (DLL + зависимости), иконки и другие ресурсы.
3. Bundle упаковывается в структуру AutoCAD Package:
   ```
   %AppData%\Autodesk\ApplicationPlugins\AutoBIMFusion\
     ├── PackageContents.xml
     ├── Contents\
     │   ├── AutoBIMFusion.dll
     │   ├── Serilog.dll
     │   ├── Serilog.Sinks.File.dll
     │   └── [другие зависимости]
     └── [иконки и ресурсы]
   ```

**Параметры развертывания:**
- **По умолчанию:** `%AppData%\Autodesk\ApplicationPlugins\` (текущий пользователь).
- **Глобально (все пользователи):** `%ProgramData%\Autodesk\ApplicationPlugins\` (если `DeployForAllUsers=true`).

## 8. Обработка ошибок и логирование

### 8.1 Стратегия обработки ошибок

- **Файл-уровень:** Ошибки при обработке конкретного DWG инкапсулируются в `MergeResult`.
  - Если файл невалидный (слишком большой, не DWG) → `MergeResult.Fail()`, операция продолжается.
  - Если экспорт/встраивание упал → `MergeResult.Fail()`, операция продолжается.

- **Команда-уровень:** Верхний `try-catch` в `MergeCommands`, `AdvancedTextCommands`, `TextStyleCommands`, `TransmittalCommands`.
  - Переводит исключение в `log.Error(ex, message)`.
  - Отправляет сообщение пользователю в командную строку.

- **Очистка:** Временные файлы удаляются в `finally`-блоках.
  - `ViewportLayoutExporter.ExportToTempAsync()` → `finally` удаляет temp DWG.
  - Если операция слияния упала на середине → Clipboard.Clear() все равно выполняется (проблема KI-3).

### 8.2 Логирование

**`OperationLogger`** выполняет двойное логирование:

1. **В командную строку AutoCAD:** `editor.WriteMessage()`.
2. **В файл:** Serilog на основе `LoggerFactory`.

**Параметры логирования:**
- **Уровни:** `Info`, `Warn`, `Error`.
- **Формат:** `[TIMESTAMP] [LEVEL] [Category] Message`.
- **Путь файла:** `Logs/merge-{date:yyyy-MM-dd}.log` рядом с assembly AutoBIMFusion.dll.
- **Ротация:** Ежедневная (новый файл для каждого дня).

**Пример файла логов:**
```
[2026-04-24 10:15:22 +05:00] [INF] AutoBIMFusion "AutoBIMFusion загружен."
[2026-04-24 10:16:05 +05:00] [INF] MergeCommands "Начало операции MERGEDWG"
[2026-04-24 10:16:08 +05:00] [INF] DwgMerger "Обработка: Drawing1.dwg"
[2026-04-24 10:16:45 +05:00] [WRN] DwgMerger "Файл Drawing2.dwg (16.2 МБ) превышает лимит 15 МБ, пропущен"
[2026-04-24 10:17:12 +05:00] [INF] MergeCommands "Операция завершена: 5 успешно, 1 пропущено, 0 ошибок"
```

## 9. Ограничения текущей реализации

### Функциональные ограничения

| Ограничение | Текущее значение | Последствие |
| :--- | :--- | :--- |
| **Max file size** | 15 МБ | Файлы больше лимита автоматически исключаются из обработки |
| **Max recursion depth** | 3 уровня | Папки глубже 3 уровней игнорируются при перечислении |
| **Обрабатываемые layout'ы** | Первый найденный | Остальные layout'ы в файле игнорируются |
| **Выходной формат** | DwgVersion.AC1032 (R2013) | Используется для совместимости с AutoCAD 2025+ |

### Технические проблемы

1. **Race condition на Clipboard** (KI-1):
   - OLE-встраивание растров использует системный Clipboard, зависит от `PASTECLIP`.
   - Другой процесс может перезаписать буфер обмена между `Clipboard.SetDataObject` и `PASTECLIP`.
   - **Риск:** Вставка неправильного изображения, потеря данных буфера.

2. **WblockCloneObjects и обработка дубликатов** (KI-2):
   - Используется `DuplicateRecordCloning.Ignore` — новые объекты получают существующие стили целевого документа, без перезаписи.
   - **Ограничение:** Если исходный файл содержит стили, отсутствующие в целевом, они не будут созданы (объекты получат ближайший существующий стиль).
   - **Риск:** Визуальные свойства объектов могут отличаться от исходного файла при несовпадении стилей.

3. **Clipboard.Clear() удаляет данные пользователя** (KI-3):
   - После встраивания растров вызывается `Clipboard.Clear()` → буфер обмена очищается навсегда.
   - **Риск:** Потеря последних скопированных пользователем данных.

4. **Поиск OLE2FRAME по Handle — хрупкий хак** (KI-4):
   - Handle в AutoCAD не гарантируется монотонно возрастающим.
   - При отсутствии Handle-а новый `Ole2Frame` останется неотмасштабированным.

5. **Fire-and-forget без обработки исключений** (KI-6):
   - `MergeDwgFolderCommand()` запускает async без `ContinueWith` обработки ошибок.
   - Если исключение вылетит до входа в `try`, оно попадет в `TaskScheduler.UnobservedTaskException`.

6. **Await внутри DocumentLock** (KI-7):
   - `await` в пределах `using (doc.LockDocument())` может вернуться в другом потоке.
   - `Dispose` пытается вызваться из пула потоков → `eLockChange`.

7. **Дублирование растровых файлов** (KI-8):
   - Если два `RasterImageDef` ссылаются на один файл → копируется N раз.

8. **ProgressMeter может зависнуть** (KI-9):
   - Если исключение в цикле `MeterProgress` → `pm.Stop()` не вызовется.
   - Прогресс-бар остается висеть в UI.

### Рекомендации по использованию

- **Нативная вставка (не в блоке):** Объекты из исходных DWG вставляются как нативные сущности напрямую в Model Space. Это ключевое требование — объекты должны быть в том же виде, как в исходном файле, не обёрнуты в `BlockReference`. Это обеспечивает редактируемость и сохранение исходной структуры.
- **Большие изображения (>5 МБ, >2500×2500):** Рекомендуется избегать OLE-встраивания; лучше оставить RasterImage с внешней ссылкой.
- **Высокая нагрузка:** Максимум 50-100 файлов в одной операции MERGEDWG для стабильности.
- **Параллельные операции:** Второй вызов MERGEDWG блокируется `SemaphoreSlim` (защита от race condition'ов в целевом документе).

## 10. Связанные документы

- [KNOWN_ISSUES.md](KNOWN_ISSUES.md) — Подтверждённые проблемы и способы их решения
- [ALGORITHM.md](ALGORITHM.md) — Математика viewport'ов и алгоритм экспорта layout'ов

---

## Версионность

- **Последнее обновление:** 2026-04-24
- **Версия документа:** 2.0
- **.NET Target:** 8.0
- **AutoCAD Support:** 2025–2027
