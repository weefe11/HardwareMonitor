# Состав репозитория

В Git входят 46 файлов. Список соответствует подготовленному индексу и финальному коммиту. Все пути ниже относительны к корню репозитория.

## Основной проект

- `HardwareMonitor/app.manifest`
- `HardwareMonitor/App.xaml`
- `HardwareMonitor/App.xaml.cs`
- `HardwareMonitor/AppSettings.cs`
- `HardwareMonitor/AssemblyInfo.cs`
- `HardwareMonitor/ColorPickerWindow.xaml`
- `HardwareMonitor/ColorPickerWindow.xaml.cs`
- `HardwareMonitor/ColorSpectrum.cs`
- `HardwareMonitor/DiagnosticsWindow.xaml`
- `HardwareMonitor/DiagnosticsWindow.xaml.cs`
- `HardwareMonitor/HardwareItem.cs`
- `HardwareMonitor/HardwareMonitor.csproj`
- `HardwareMonitor/HardwareSampler.cs`
- `HardwareMonitor/HotkeyManager.cs`
- `HardwareMonitor/MainWindow.xaml`
- `HardwareMonitor/MainWindow.xaml.cs`
- `HardwareMonitor/OutlinedText.cs`
- `HardwareMonitor/OverlayRows.cs`
- `HardwareMonitor/OverlayWindow.xaml`
- `HardwareMonitor/OverlayWindow.xaml.cs`
- `HardwareMonitor/SensorCatalog.cs`
- `HardwareMonitor/SensorItem.cs`
- `HardwareMonitor/TemperatureAlert.cs`

## Тестовый проект

- `HardwareMonitor.Tests/HardwareMonitor.Tests.csproj`
- `HardwareMonitor.Tests/Program.cs`
- `HardwareMonitor.Tests/RegressionTests.cs`

## Ресурсы и иконки

- `HardwareMonitor/Assets/app.ico`
- `HardwareMonitor/Assets/AppIcon.xaml`
- `tools/New-AppIcon.ps1`

## Скриншоты

- `Screenshots/appearance.png`
- `Screenshots/color-picker.png`
- `Screenshots/main-dark.png`
- `Screenshots/main-light.png`
- `Screenshots/overlay-details.png`
- `Screenshots/overlay.png`

## Документация

- `CHANGELOG.md`
- `CHANGES.md`
- `docs/GITHUB.md`
- `docs/LAPTOP_CHECKLIST.md`
- `docs/REPOSITORY_MANIFEST.md`
- `docs/THIRD_PARTY.md`
- `README.md`

## GitHub Actions

- `.github/workflows/windows-ci.yml`

## Конфигурация и решение

- `.gitattributes`
- `.gitignore`
- `HardwareMonitor.sln`

## Намеренно исключено

- `bin/`, `obj/`, `.vs/`, `.vscode/`, `.idea/`, `TestResults/` — сборка, кэш и локальная среда.
- `.artifacts/`, `artifacts/` — чистая проверочная копия, готовый Release/ZIP, аппаратные отчёты, аудит и тестовые настройки.
- `settings.json`, `appsettings.Local.json`, `*.user`, `*.suo`, `*.sln.docstates` и другие пользовательские файлы IDE.
- `.env*`, ключи/сертификаты, `*.log`, `*.tmp`, `*.bak`, пакеты NuGet и системные временные файлы; полные правила — в `.gitignore`.
- Дипломные документы, установщик/драйвер PawnIO и исходники зависимостей не входят в проект; зависимости восстанавливаются из NuGet.

Папка `.git/` хранит историю локально и не является исходным файлом. Собственная `LICENSE` пока не добавлена: решение принимает владелец. Лицензии зависимостей перечислены в `docs/THIRD_PARTY.md`.

Сверка после clone: `git ls-files` должен давать этот список; `git status --short` — пустой результат после сборки и тестов. Новые будущие изменения могут закономерно изменить список.
