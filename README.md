# AutoBIMFusion

Плагин AutoCAD .NET (2019–2027, Windows x64). Команда `MERGEDWG` рекурсивно находит DWG в выбранной папке, экспортирует первый Layout каждого файла в Model Space и вставляет результат в текущий чертеж как нативные объекты AutoCAD.

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
C:\Users\y.zhumabayev\Repository\AutoBIMFusion\tools\Start-MergeDwgBatch.ps1
```

> Если скрипт не запускается из-за политики выполнения, сначала выполните:
> ```powershell
> Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
> ```

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

Для сборки нужен .NET SDK 10.0.300 или новее (C# 14, формат `.slnx`).
Reference assemblies .NET Framework восстанавливаются автоматически через NuGet;
установка старых Developer Pack не обязательна.

Для каждого года доступны `DebugAxx` и `ReleaseAxx`:

| AutoCAD | Суффикс | .NET для запуска | SDK AutoCAD | Series |
|---|---|---|---|---|
| 2019 | A19 | .NET Framework 4.7 | 23.0.* | R23.0 |
| 2020 | A20 | .NET Framework 4.7 | 23.1.* | R23.1 |
| 2021 | A21 | .NET Framework 4.8 | 24.0.* | R24.0 |
| 2022 | A22 | .NET Framework 4.8 | 24.1.* | R24.1 |
| 2023 | A23 | .NET Framework 4.8 | 24.2.* | R24.2 |
| 2024 | A24 | .NET Framework 4.8 | 24.3.* | R24.3 |
| 2025 | A25 | .NET 8 | 25.0.* | R25.0 |
| 2026 | A26 | .NET 8 | [25.1.0, 25.1.1) | R25.1 |
| 2027 | A27 | .NET 10 | 26.0.* | R26.0 |

Например, `dotnet build AutoBIMFusion.slnx -c ReleaseA19` собирает плагин для 2019.
Для отладки задайте `ACAD_HOME` — каталог соответствующего установленного AutoCAD.
Для пакетного скрипта передавайте согласованные `-Configuration` и `-AutoCADRoot`.
Логирование Serilog сохранено во всех версиях. В A19–A24 пакет Polyfill добавляет
совместимые API в исходном виде, `System.Text.Json` поставляется с зависимостями,
а графика использует встроенный `System.Drawing` .NET Framework.
Неиспользуемая COM-ссылка `AutoCAD.NET.Interop` в A19–A24 отключена.
В A19–A24 `LegacyDependencyResolver` загружается до команд и разрешает старые ссылки
на известные BCL-зависимости из папки плагина. Файлы `acad.exe.config` и
`accoreconsole.exe.config` не меняются: конфигурация DLL не применяется хостом.

Обычная сборка автоустанавливается в `%AppData%\Autodesk\ApplicationPlugins\AutoBIMFusion.bundle` и предназначена строго для выбранного года. DLL для .NET Framework и .NET 8/10 невзаимозаменяемы.

Результаты desktop/headless разделены в `bin/<режим>/x64/<конфигурация>/<framework>/`,
промежуточные файлы и NuGet assets — в отдельных каталогах `obj`.
Headless не устанавливается автоматически и не удаляет обычный пакет при очистке.
`-p:DisableAutoCADDeployment=true` создаёт локальный bundle без установки.
Установщик всегда собирает свежий desktop-пакет. Общий скрипт публикации сначала
копирует и проверяет весь пакет, затем заменяет каталог с резервной копией и откатом
при ошибке замены. Перед обновлением закройте AutoCAD, после обновления запустите заново.

Для ручной установки:

```powershell
.\tools\Install-AutoBIMFusionBundle.ps1 -Configuration ReleaseA19
```

## Проверка совместимости

```powershell
# Все 36 сборок: 9 лет × Debug/Release × desktop/headless.
# Развёртывание изолировано в out/compatibility/deploy.
.\tools\Test-AutoCADBuildMatrix.ps1

# Установка: замена пакета, неполная копия, заблокированная DLL, параллельный запуск.
.\tools\Test-AutoCADBundlePublication.ps1

# Утилиты, очередь приоритетов, Serilog и JSON без установленного AutoCAD.
dotnet run --project tests/AutoBIMFusion.Compatibility.Tests -c DebugA19
dotnet run --project tests/AutoBIMFusion.Compatibility.Tests -c DebugA24
dotnet run --project tests/AutoBIMFusion.Compatibility.Tests -c DebugA26
dotnet run --project tests/AutoBIMFusion.Compatibility.Tests -c DebugA27

# Проверка в установленном AutoCAD: создаёт DWG, объединяет, открывает результат
# и проверяет объекты и новую запись журнала именно этого запуска.
# Core Console запускается с /isolate: рабочий профиль AutoCAD не меняется.
.\tools\Test-AutoCADHost.ps1 -Configuration DebugA19 -AutoCADRoot $env:ACAD_HOME

# Сборка без Ribbon/WPF для accoreconsole.exe.
dotnet build AutoBIMFusion.slnx -c DebugA19 -p:CoreConsoleDiagnostics=true
```

`MERGEDWG_BATCH` в Core Console требует открытого пустого безымянного чертежа:
создание нового документа через оконный API там недоступно. Обычная сборка
сохраняет создание документа при необходимости. Консольная проверка использует
свой bundle и не заменяет установленную версию с лентой AutoCAD.

Компиляция по всем SDK не заменяет проверку на реальных чертежах в каждом AutoCAD.
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

Логи слияния: папка «Документы» пользователя, `AutoBIMFusion\Logs\merge-YYYY-MM-DD.log`.

## Лицензия

См. [LICENSE.txt](LICENSE.txt).
