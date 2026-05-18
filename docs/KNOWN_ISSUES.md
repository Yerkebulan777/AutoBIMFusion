# Известные проблемы и решения

**Последнее обновление:** 2026-05-15

Файл содержит актуальные риски и спорные архитектурные решения. Активная AutoCAD-команда: `MERGEDWG`. Команды `SMART_MERGE_TEXT`, `MERGE_TEXT_STYLES`, `JOIN_LINES` и `CREATE_ETRANSMIT_ZIP` находятся в `src/AutoBIMFusion.Plugin/Commands/Archive` и исключены из сборки.

---

## Открытые проблемы

### KI-1. `DuplicateRecordCloning.Ignore` может унаследовать целевые стили

**Где:** `BlockInserter.InsertNativeObjects`

`WblockCloneObjects` с `DuplicateRecordCloning.Ignore` избегает дублирования стилей и слоёв в целевой базе. Но если исходный стиль совпадает по имени с целевым, а параметры различаются, клонированные объекты унаследуют целевое определение.

**Решение:** Оставить текущее поведение ради стабильности целевой базы или перейти на `MangleName`, если потребуется точное воспроизведение внешнего вида исходников.

---

### KI-2. Нет отмены для длинных операций

**Где:** `CombineCommands`, `CombineOrchestrator`, `LayoutProjectionProcessor`

При большом количестве файлов операция может выполняться долго без возможности отмены.

**Решение:** Добавить `CancellationToken` через весь пайплайн с проверкой между файлами и перед тяжёлыми операциями листа.

---

### KI-3. Жёстко заданные операционные ограничения

| Ограничение | Значение | Место |
|---|---|---|
| Максимальный размер файла | 15 МБ | `FileUtil.GetFiles` |
| Глубина рекурсии в папках | 3 | `FileUtil.GetFiles` |
| Рабочая нормализация масштаба VP | 1:100 | `LayoutProjectionProcessor` |
| Purge passes | 10 | `DrawingPurger.Optimize` |
| Зазор между вставленными листами | 10%, минимум 1.0 | `BlockInserter` |

Значения консервативны и не настраиваются пользователем.

**Решение:** Добавить конфигурационный файл только если реальные проекты потребуют других порогов.

---

### KI-4. Визуальное качество размеров требует ручной проверки

**Серьёзность:** Средняя

**Где:** `DimensionStyleNormalizer`, `LayoutProjectionProcessor`

Алгоритм приводит главный viewport к рабочему масштабу `1:100`. Скопированные размеры получают чистый стиль AutoBIM, DSTYLE overrides очищаются, `Dimscale` становится равным `100`, а числовая поправка измерения хранится на экземпляре размера через `Dimlfac = 1 / geometryScale`.

**Риск:** После масштабирования Model Space, переноса Paper Space или трансформации aux vpt высота текста, размер стрелок или числовое значение размера может не совпасть с оригиналом.

**Статус:** Диагностика (`[DIM-STYLE]`, viewport-normalize logs, snapshots) пишется в лог. Требуется ручная визуальная QA на репрезентативных листах, включая проверку отсутствия `304.8` в `Dimscale`; `Dimscale` должен быть `100`, а `Dimlfac` — обратным коэффициентом нормализации геометрии.

---

### KI-5. Headless diagnostic script вызывает отсутствующую команду

**Серьёзность:** Высокая для диагностики, низкая для desktop-команд.

**Где:** `tools/Run-MergeDwgDiagTest.ps1`

Скрипт строит core bundle и запускает `MERGEDWG_DIAG_TEST` в `accoreconsole.exe`, но в текущем коде нет `[CommandMethod("MERGEDWG_DIAG_TEST")]`.

**Решение:** Восстановить диагностическую команду в C# с учетом `CoreConsoleDiagnostics=true` или изменить скрипт на существующую команду/новый diagnostic entry point. До этого скрипт не использовать как acceptance gate.

---

### KI-6. PhantomBlockCleaner false positives

**Серьёзность:** Средняя

**Где:** `PhantomBlockCleaner.FindPhantomBlocks`

Алгоритм удаляет блоки с диагональю BoundingBox ≤15 units и максимальным расстоянием углов BoundingBox от origin выше threshold. Легитимные микро-блоки (детали, символы) с большим смещением могут быть ошибочно классифицированы как фантомные.

**Решение:** Добавить whitelist или configurable thresholds если потребуется для конкретных проектов.

---

### KI-7. BlockBasePointEditor пропускает dynamic/anonymous blocks

**Серьёзность:** Низкая

**Где:** `BlockBasePointEditor.ShouldSkipBlockDefinition`

Dynamic blocks, anonymous blocks и xref блоки пропускаются нормализацией базовых точек. Если такие блоки имеют смещённые base points, они могут вызвать misalignment при слиянии.

**Решение:** Добавить поддержку dynamic blocks если потребуется.

---

## Исправлено

| Проблема | Исправление |
|---|---|
| Скрытое масштабирование при `WblockCloneObjects` (метрика ↔ имперская) | `DatabaseUnitSyncScope` временно выравнивает source с target; `DimensionStyleNormalizer` дополнительно удаляет DSTYLE overrides у скопированных размеров. |
| Тяжёлая диагностика размерных стилей | `DimensionStyleDiagnosticUtils` выполняет снимки только при `LOG_LEVEL=DEBUG`. |
| Класс-обёртка `FolderSelector` | Удалён; `CombineCommands` вызывает `UiDialogService` напрямую. |
| Дублирование scope-логики системных переменных | Общая логика вынесена в `ManagedSystemVariable` / scope helpers. |
| Двойные транзакции при поиске Paper Space entities | Логика сосредоточена в `LayoutUtil`. |
| Дрейф масштаба aux vpt | Трансформация aux-to-main использует исходный масштаб main vpt до global clamp. |
| Удвоенная нормализация размеров | Нормализация выполняется в обработке конкретного Viewport до aux-клонирования. |
| Авто-масштаб на имперских DWG | Синхронизация units/measurement выполняется после `CloseInput(true)`, а размеры получают Viewport-специфичные стили до clone. |
| Остатки оригиналов aux vpt | Добавлено удаление объектов вне main window. |
| `ProgressMeter` не закрывался при ошибке | `pm.Stop()` вызывается в `finally`. |
| Phantom block corruption extents | `PhantomBlockCleaner` (BoundingBox detection) |
| Block base point offset | `BlockBasePointEditor` (bottom-left normalization) |
| Non-uniform block scale distortion | `BlockScaleApplier` (definition + reference scaling) |

---

## Открытые вопросы

1. Стоит ли сохранять стили/слои исходных чертежей точно, даже если это создаёт коллизии имён в целевом DWG?
2. Добавить обработку всех листов или оставить команду работающей только с первым Layout?
3. Сделать операционные ограничения настраиваемыми или оставить фиксированными для предсказуемого поведения?
4. Восстанавливать `MERGEDWG_DIAG_TEST` или заменить headless diagnostic на другой entry point?
