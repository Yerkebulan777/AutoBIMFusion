# Техническая документация AutoBIMFusion

**Последнее обновление:** 2026-05-04

## 1. Обзор

AutoBIMFusion — плагин для AutoCAD (.NET 8, x64, AutoCAD 2025–2027) для автоматизации объединения чертежей, очистки текстов и стилей, а также подготовки пакетов eTransmit.

## 2. Структура проекта

```text
AutoBIMFusion/
  Application/
    AcadSupport/      # RAII-скоупы системных переменных AutoCAD
    Commands/         # Точки входа (команды AutoCAD)
    Combine/          # Пайплайн объединения DWG
      Layouts/        # Обработка листов, видовых экранов, трансформаций, размеров
    Ribbon/           # Лента AutoCAD (исключается при CoreConsoleDiagnostics=true)
    Utils/            # Вспомогательные утилиты (файлы, строки, диалоги, сортировка)
  Infrastructure/
    Logging/          # Serilog + DiagnosticSink (Trace / Debug)
```

## 3. Ключевые классы

| Класс | Назначение |
|---|---|
| `CombineCommands` | Точка входа MERGEDWG; семафор, прогресс, сохранение |
| `CombineOrchestrator` | Координирует обработку одного файла |
| `ViewportLayoutExporter` | Открывает DWG в памяти, проецирует лист → модель |
| `LayoutProjectionProcessor` | Масштабирование VP, трансформация aux-VP и Paper Space |
| `ViewportTransformer` | Математика трансформаций, клонирование, DrawOrder |
| `BlockInserter` | `WblockCloneObjects` + расстановка по оси X |
| `DimensionStyleNormalizer` | Нормализация размерных стилей перед клонированием |
| `DimensionStyleDiagnosticUtils` | Диагностический снимок стилей в лог |
| `DwgOptimizer` | Многопроходный Purge |
| `RasterImagePathFixer` | Копирование растров, обновление путей |
| `ExtentsUtils` | Математика с Extents3d (без API-вызовов) |
| `ModelSpaceTrimmer` | Удаление объектов вне рамки листа |

## 4. Управление ресурсами

- **Транзакции:** `using Transaction tr = ...` — всегда. `tr.Commit()` или `tr.Abort()` в явном виде.
- **Блокировки:** `using (doc.LockDocument())` — обязательно для любой записи в активный документ.
- **Системные переменные:** `AcadWarningSuppressScope` и `AcadUnitScalingOverrideScope` — RAII-скоупы на базе `SysVarScope`. Гарантируют восстановление переменных даже при исключении.
- **Фоновые базы данных:** `new Database(false, true)` открывается в памяти, передаётся через `using Database? db = ...`, уничтожается сразу после обработки файла.
- **Единицы измерения:** перед `WblockCloneObjects` принудительно синхронизируются `Insunits` и `Measurement` через `ExtentsUtils.SyncUnits`.

## 5. Обработка ошибок и логирование

- Логгер — Serilog `Logger` из `LoggerFactory.GetSharedLogger()`. Файл лога — `{AssemblyDir}/Logs/merge-{date}.log`.
- `DiagnosticSink` дублирует записи в `Debug.WriteLine` (при отладке) или `Trace.WriteLine` (в production).
- Исключения AutoCAD API перехватываются точечно там, где сбой одного объекта не должен останавливать пакет.
- В циклах суммируются счётчики; подробный лог пишется только при наличии ошибок.

## 6. Ключевые константы

| Константа | Значение | Описание |
|---|---|---|
| `MaxFileSizeBytes` | 15 МБ | Пропуск тяжёлых файлов |
| `MaxRecursionDepth` | 3 | Глубина поиска в папках |
| `MaxScaleMultiplier` | 100 | Ограничение масштаба VP (1:100) |
| `MaxPurgePasses` | 5 | Лимит итераций очистки |
| `gapPercent` | 0.1 | Зазор между чертежами (10% от габарита) |
