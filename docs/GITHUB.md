# Публикация и выпуск v1.0.0

Репозиторий: https://github.com/weefe11/HardwareMonitor. Основная ветка — `main`, решение — `HardwareMonitor.sln`. Репозиторий уже создан; существующий `origin` не нужно добавлять повторно.

**Описание для GitHub:** Русскоязычный мониторинг компьютера для игр и работы: лёгкий прозрачный виджет, выбор показателей и диагностика аппаратных датчиков.

**Темы:** `csharp`, `dotnet`, `wpf`, `windows`, `hardware-monitoring`, `overlay`, `librehardwaremonitor`, `pawnio`.

## Получить исходники и проверить

Нужны Windows, Git for Windows и .NET SDK 8 или новее. Для приватного репозитория используйте учётную запись с доступом; авторизацию выполняйте через браузер/менеджер учётных данных, без пароля или токена в URL.

```powershell
git clone https://github.com/weefe11/HardwareMonitor.git
cd HardwareMonitor
dotnet restore
dotnet build -c Release --no-restore -warnaserror
dotnet test -c Release --no-build --no-restore
& ./HardwareMonitor/bin/Release/net8.0-windows/HardwareMonitor.exe
```

Если clone уже есть: `git status`, затем `git pull --ff-only` при чистом рабочем дереве. Не переносите чужие `bin`, `obj` и настройки. Для аппаратных датчиков установите при необходимости подписанный [PawnIO](https://pawnio.eu/) и подтвердите UAC при запуске EXE. После установки драйвера перезапустите приложение.

Если Git не доступен в PATH, установите Git for Windows и откройте новый терминал; в Visual Studio можно использовать её встроенный Git. [Подтверждённое оборудование](TESTED_HARDWARE.md), [список исходников](REPOSITORY_MANIFEST.md).

## Внести изменение и отправить в main

Закройте приложение через «Выход» в трее перед сборкой. На новом ПК настройте своё имя и адрес автора локально для репозитория через `git config user.name` и `git config user.email`; можно использовать GitHub noreply.

```powershell
git status
git pull --ff-only
# Внесите исправления.
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
git add <изменённые-файлы>
git commit -m "Describe the change"
git push origin main
```

Проверьте `git remote -v`: он должен указывать на ваш репозиторий. Если origin отсутствует только в новой самостоятельно созданной копии, используйте `git remote add origin https://github.com/weefe11/HardwareMonitor.git`. Не используйте force push. Настройки, аппаратные отчёты, логи и готовые сборки не добавляйте в исходный Git.

## Проверить GitHub Actions

Откройте **Actions → Windows CI → запуск для отправленного коммита**. Нужен зелёный результат `build-and-test`: restore → Release с `-warnaserror` → 13 NUnit-сценариев. При ошибке откройте упавший шаг; результаты тестов доступны в артефакте `windows-test-results`. Успех предыдущего коммита не доказывает успех нового.

CI использует моделируемые датчики и настоящие элементы WPF, без PawnIO и реальной аппаратной проверки. В приватном репозитории значок может быть недоступен без авторизации. [Журналы Actions](https://docs.github.com/en/actions/how-tos/monitor-workflows/use-workflow-run-logs).

## Готовая сборка

Для обычного запуска используйте ZIP приложения из [Releases](https://github.com/weefe11/HardwareMonitor/releases). Распакуйте все файлы: EXE требует соседние DLL. Нужны **Windows x64 и .NET Desktop Runtime 8 x64**; Runtime и PawnIO в архив не входят. Не используйте папку проверки `package-smoke`: она содержит инструменты тестирования, которых нет в пользовательском пакете.

Воспроизводимая подготовка пакета:

```powershell
dotnet publish HardwareMonitor/HardwareMonitor.csproj -c Release -r win-x64 --self-contained false -o .artifacts/v1.0.0-release/HardwareMonitor -p:Version=1.0.0 -p:DebugType=None -p:DebugSymbols=false -warnaserror
```

При архивировании сохраните подготовленные `ПРОЧИТАТЬ.txt`, `THIRD_PARTY.md`, `BUILD-INFO.txt` и папку `Licenses`. В `BUILD-INFO.txt` укажите фактический commit сборки, версию и требования. Тестовые DLL/EXE и PDB в пользовательский ZIP не включайте. Сборочные каталоги и ZIP исключены из исходного Git; их место — GitHub Releases.

## Опубликовать стабильный v1.0.0

После зелёного CI для текущего main откройте **Releases → Draft a new release**. Выберите новый тег **v1.0.0** на проверенном коммите ветки main. Не перемещайте уже существующий тег.

Название: **Мониторинг ПК 1.0.0**. Возьмите краткое описание из CHANGELOG, прикрепите `HardwareMonitor-v1.0.0-win-x64.zip` и файл SHA-256. Укажите Runtime 8 x64, отдельную установку PawnIO и ссылку на проверенное оборудование. Флажок prerelease для стабильного v1.0.0 отключите. Можно сначала сохранить черновик.

## Сделать репозиторий публичным

Просмотрите историю и убедитесь, что в ней нет секретов/личных отчётов. В **Settings → General → Danger Zone → Change repository visibility → Change to public** подтвердите смену видимости. [Инструкция GitHub](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/managing-repository-settings/setting-repository-visibility).

Проверки двух компьютеров завершены; ограничения перечислены в README и TESTED_HARDWARE. Публикация Release и смена видимости остаются отдельными действиями владельца. Собственная лицензия пока не выбрана: для свободного использования собственного кода подходит MIT, если владелец согласен с её условиями. Публичная видимость не заменяет лицензию; лицензии зависимостей действуют отдельно.
