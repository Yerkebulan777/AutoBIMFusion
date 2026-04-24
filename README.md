# AutoBIMFusion

**AutoBIMFusion** — это плагин для AutoCAD, разработанный для пакетного объединения нескольких DWG-файлов в один чертёж. Он автоматически обрабатывает все DWG-файлы из выбранной папки (включая подпапки), экспортирует первый Paper Space лист каждого файла и размещает их в итоговом чертеже вдоль оси X с автоматическими отступами.

---

## ⚡ Основные возможности

- **Пакетное объединение DWG:** Обработка всех файлов из выбранной папки и её подпапок в один чертёж одной командой.
- **Умное объединение `TEXT` в `MText`:** Команда `SMART_MERGE_TEXT` собирает близко расположенные фрагменты в связный многострочный текст.
- **Объединение дубликатов текстовых стилей:** Команда `MergeTextStyles` находит идентичные стили, переназначает объекты на мастер-стиль и удаляет дубликаты.
- **Пакет eTransmit в ZIP:** Команда `CreateETransmitZip` собирает зависимости текущего чертежа (Xref, шрифты, изображения, plot/data links) и формирует ZIP-архив.
- **Экспорт первого Layout:** Из каждого исходного файла экспортируется первый Paper Space лист через viewport-aware стратегию (2+ VP — матрица трансформации, 1 VP — клонирование через главный viewport, 0 VP — масштабирование ×100).
- **Автоматическое размещение:** Содержимое файлов вставляется как блоки вдоль оси X с отступом 10% от размера объекта (настраивается).
- **Внедрение Xref:** Файлы временно подключаются как внешние ссылки и сразу внедряются (Bind) в итоговый чертёж.
- **Бесшовная интеграция:** Автоматическая загрузка в AutoCAD через стандартный механизм `ApplicationPlugins`.
- **Логирование:** Подробный вывод хода операции в редактор AutoCAD с прогресс-баром.

## 📚 Документация

- Техническая документация: [AutoBIMFusion/docs/TECHNICAL_DOCUMENTATION.md](AutoBIMFusion/docs/TECHNICAL_DOCUMENTATION.md)
- Детальный алгоритм merge-пайплайна: [AutoBIMFusion/docs/ALGORITHM.md](AutoBIMFusion/docs/ALGORITHM.md)

## ✨ Последние улучшения

- **Исправление OLE для крупных JPG/PNG (2026-04-23):** добавлен fallback ресайза через `Ole2Frame.Position3d`, если `WcsWidth/WcsHeight` не применились (наблюдалось как гигантские размеры `OLE2FRAME` у отдельных листов). Теперь после `PASTECLIP` объект принудительно приводится к целевым габаритам рамки и только затем выравнивается по позиции.
- **Встраивание растров (OLE) и draw order:** Все внешние растровые изображения (`RasterImage`) конвертируются и внедряются внутрь объединённого чертежа как объекты `OLE2FRAME` через системный буфер обмена (команда `._PASTECLIP`). Это полностью отвязывает итоговый DWG от внешних файлов изображений. Порядок отрисовки (`SortentsTable`) сохраняется при клонировании Paper Space → Model Space.
- **`GeometryUtils.Union`:** метод объединения AABB перенесён из `ModelSpaceTrimmer` в общий утилитный класс — `ModelSpaceTrimmer.ComputeBounds` теперь использует `GeometryUtils.Union` вместо приватной копии.
- **`ClampMainVpScale`:** дублированная логика ограничения масштаба VP (присутствовала отдельно в `ProcessSingleVp` и `ProcessMultiVp`) объединена в один приватный метод; оба пути теперь вызывают `ClampMainVpScale(...)`.
- **`ALGORITHM.md` актуализирован:** корректные имена классов (`ViewportLayoutExporter`, `LayoutUtil.TryFindFirstLayout`), добавлена секция математики трансформаций, убраны устаревшие блоки с некорректным контентом.
- **Оптимизация multi-viewport:** кэширование `ModelEntitySnapshot` устраняет повторные проходы по Model Space при обработке каждого aux-viewport (O(n·m) → O(n)).
- **Корректная асинхронность команды `MERGEDWG`:** убран опасный `async void`, добавлен `SemaphoreSlim` для защиты от повторного запуска.
- **Надёжность временных файлов:** `tempPath` теперь локальная переменная вместо поля класса — исключены утечки при исключениях между итерациями.
- **Удаление мёртвого кода и классов-оберток:** убраны неиспользуемые `ExportWarningSuppressScope`, `PaperToModelChspace`, пустой `ImageToOle.cs`. Устранены классы-обертки `MergeSaver`, `MainViewportSelector`, `MessageUtil` — их логика перенесена в соответствующие места использования для упрощения структуры (Flatter Architecture).
- **Исправление `FileEnumerator`:** фильтр по префиксу теперь проверяет имя файла, а не полный путь; убраны лишние проходы LINQ.
- **Ribbon:** вкладка больше не захватывает фокус при старте (`IsActive = true` удалено).

## 🛠 Требования

- **AutoCAD:** 2025, 2026, 2027 (64-bit).
- **OS:** Windows 8.0+ (x64).
- **Runtime:** [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).

---

## 🚀 Быстрый старт

### Установка и сборка

1. Клонируйте репозиторий.
2. Откройте `AutoBIMFusion.slnx` в Visual Studio.
3. Скомпилируйте решение в конфигурации **ReleaseA25** / **ReleaseA26** / **ReleaseA27** (для нужной версии AutoCAD).
4. После успешной сборки плагин будет автоматически скопирован в папку плагинов Autodesk:
   `%AppData%\Autodesk\ApplicationPlugins\AutoBIMFusion.bundle`

### Использование

1. Запустите AutoCAD. Плагин загрузится автоматически и создаст собственную вкладку на ленте.
2. Введите команду **`MERGEDWG`**.
3. Выберите папку, содержащую DWG-файлы для объединения.
4. Плагин обработает все файлы и сохранит результат в родительскую директорию с именем `<Имя_папки>.dwg`.

---

## ⌨️ Команды

| Команда | Описание |
| :--- | :--- |
| `MERGEDWG` | **Merge DWG Files**: Объединяет все DWG-файлы из выбранной папки в один чертёж. |
| `SMART_MERGE_TEXT` | Объединяет соседние `TEXT` (по стилю, высоте и геометрической близости) в единый `MText` в Model Space. |
| `MergeTextStyles` | Находит и сливает дубликаты текстовых стилей (`TextStyle`), обновляя `DBText`, `MText`, `AttributeDefinition`, `AttributeReference`. |
| `CreateETransmitZip` | Собирает пакет зависимостей текущего DWG через eTransmit и сохраняет ZIP в папку `ETransmitOutput` рядом с чертежом. |

---

## 🏗 Архитектура проекта

| Модуль | Назначение |
| :--- | :--- |
| **Application/Commands** | Точки входа — команды AutoCAD (`MERGEDWG`, `SMART_MERGE_TEXT`, `MergeTextStyles`, `CreateETransmitZip`). |
| **Application/Merge** | Ядро объединения: `DwgMerger`, `BlockInserter`. Сохранение вынесено в `MergeCommands`. |
| **Application/Merge/Layouts** | Обработка viewport'ов: `ViewportLayoutExporter`, `ViewportTransformer`, `ModelSpaceTrimmer`, `ViewportCollector`. Выбор главного VP вынесен в `LayoutViewportInfo`. |
| **Application/Merge** | Модели данных: `MergeResult`, `MergeStatistics`. |
| **Application/Ribbon** | Создание вкладки на ленте AutoCAD. |
| **Application/Utils** | Вспомогательные утилиты: `FileEnumerator`, `FileHelper`, `LayoutUtil`, `FolderSelector`, `WindowsNaturalComparer`. |
| **Application/Merge/Layouts** | Геометрические утилиты: `GeometryUtils` (форматирование, AABB-пересечения, безопасное чтение `GeometricExtents`). |
| **Infrastructure/Logging** | Логирование операций через `OperationLogger`. |

Полное и актуализированное описание архитектуры, моделей данных, конфигурации окружений, обработки ошибок, логирования и процесса контрибьютинга доступно в технической документации: [AutoBIMFusion/docs/TECHNICAL_DOCUMENTATION.md](AutoBIMFusion/docs/TECHNICAL_DOCUMENTATION.md).

---

## 📐 Принципы проектирования

- **Прямолинейность (Flatter Architecture):** Избегание излишних слоёв абстракций, обёрток и глубокой вложенности. Использование прямых вызовов методов вместо создания промежуточных классов.
- **Явные проверки (Explicit Validation):** Использование `ArgumentNullException.ThrowIfNull` для валидации аргументов.
- **Отсутствие локальных функций:** Предпочтение отдаётся обычным методам или линейному коду без вложенных методов (local functions).
- **Локализация утилит:** Специализированные утилиты выделены в отдельные классы: геометрия — `GeometryUtils`, файлы — `FileHelper`.

---

## 📄 Лицензия

Проект распространяется под лицензией **MIT**. Подробности в файле [LICENSE.txt](LICENSE.txt).

