# Публикация на GitHub

Локальное решение: `HardwareMonitor.sln`. Основная ветка — `main`. Перед первым push проверьте `git status` и `git log -1`: финальный коммит называется `Prepare Hardware Monitor for public release`. Remote автоматически не создавался.

**Название:** HardwareMonitor
**Описание:** Русскоязычный мониторинг компьютера для игр и работы: лёгкий прозрачный виджет, выбор показателей и диагностика аппаратных датчиков.
**Темы GitHub:** `csharp`, `dotnet`, `wpf`, `windows`, `hardware-monitoring`, `overlay`, `librehardwaremonitor`, `pawnio`.

## 1. Создать приватный репозиторий

Войдите на GitHub → **+ → New repository**. Название: `HardwareMonitor`. Выберите **Private**. Не добавляйте README, `.gitignore` и лицензию: исходники уже подготовлены локально. Нажмите **Create repository** и скопируйте HTTPS-адрес из блока **Quick setup**.

В локальном README замените обе ссылки `YOUR_LOGIN/HardwareMonitor` в значке CI на фактический путь нового репозитория. Если название другое, замените также `HardwareMonitor` в этих ссылках.

## 2. Привязать локальный проект и отправить

Откройте PowerShell в папке исходников. Ниже предполагается, что Git установлен и доступен командой `git`.

```powershell
git status
git branch -M main
git remote add origin https://github.com/YOUR_LOGIN/HardwareMonitor.git
git add README.md
git commit -m "Set repository CI badge"
git push -u origin main
```

Подставьте адрес своего репозитория. Если README не менялся, пропустите `add`/`commit`. Если `origin` уже существует, проверьте `git remote -v`; для замены используйте `git remote set-url origin <URL>`, а не повторный `remote add`. Не добавляйте пароль или токен в URL. При запросе авторизации используйте браузер/менеджер учётных данных Git.

Если Git не найден, установите Git for Windows и откройте новый PowerShell. Либо найдите Git в установленной Visual Studio и задайте временный псевдоним:

```powershell
$gitExecutable = Get-ChildItem 'C:/Program Files/Microsoft Visual Studio' -Filter git.exe -Recurse -ErrorAction SilentlyContinue |
    Where-Object FullName -Like '*/Team Explorer/Git/cmd/git.exe' | Select-Object -First 1 -ExpandProperty FullName
Set-Alias git $gitExecutable
```

Полный список исходников: [manifest](REPOSITORY_MANIFEST.md). Сборочные папки, настройки и отчёты в Git не входят.

## 3. Проверить автоматическую сборку

На странице репозитория откройте **Actions → Windows CI → последний запуск**. Дождитесь зелёного результата задачи `build-and-test`. Она выполняет `restore`, Release-сборку с `-warnaserror` и `dotnet test`. При красном результате откройте упавший шаг и его журнал; результаты тестов доступны в артефакте `windows-test-results`.

Проверки CI используют моделируемые аппаратные данные и настоящие элементы WPF. Системный драйвер PawnIO на машине CI не требуется. Аппаратный тест вашего компьютера CI не заменяет. Значок приватного репозитория может не отображаться без авторизации. [Журналы Actions](https://docs.github.com/en/actions/how-tos/monitor-workflows/use-workflow-run-logs).

## 4. Клонировать на второй компьютер

Установите Windows 10/11, Git for Windows и .NET SDK 8 или новее. Для проверки аппаратных датчиков отдельно установите подписанный [PawnIO](https://pawnio.eu/), если нужен низкоуровневый доступ. Войдите в GitHub под учётной записью, имеющей доступ к приватному репозиторию.

```powershell
git clone https://github.com/YOUR_LOGIN/HardwareMonitor.git
cd HardwareMonitor
dotnet restore
dotnet build -c Release --no-restore -warnaserror
dotnet test -c Release --no-build --no-restore
& ./HardwareMonitor/bin/Release/net8.0-windows/HardwareMonitor.exe
```

Подтвердите UAC для приложения. Установка PawnIO не выполняется NuGet-пакетом; после установки драйвера перезапустите приложение. Следуйте [короткому списку проверки ноутбука](LAPTOP_CHECKLIST.md). Не копируйте старые `bin`, `obj` или локальные настройки с первого ПК.

## 5. Исправить что-либо на ноутбуке

Закройте приложение через «Выход» в трее перед сборкой. Если это первый коммит на втором ПК, настройте автора локально:

```powershell
git config user.name "Konstantin Karasev"
git config user.email "YOUR_EMAIL_OR_GITHUB_NOREPLY"
git pull --ff-only
git status
# Внесите нужные исправления.
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
git add <изменённые-файлы>
git commit -m "Fix laptop compatibility issue"
git push
```

Не добавляйте диагностические отчёты и настройки пользователя. На первом ПК получите изменения командой `git pull --ff-only`, предварительно проверив чистоту рабочего дерева.

## 6. Открыть публичный доступ

После зелёного CI и ручной проверки откройте **Settings → General → Danger Zone → Change repository visibility → Change to public**. Проверьте предупреждения GitHub и подтвердите действие. Перед этим просмотрите все коммиты: публичной станет история, а не только последняя версия. [Инструкция GitHub](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/managing-repository-settings/setting-repository-visibility).

Добавьте описание и темы в **About**. Собственная лицензия ещё не выбрана: для свободного использования собственного кода можно выбрать MIT и добавить `LICENSE` с автором/годом. Лицензии зависимостей действуют отдельно. Публичная видимость сама по себе не заменяет лицензию.

## 7. Готовая версия и первый Release v1.0.0

Локально подготовлен ZIP в `.artifacts/release/HardwareMonitor-v1.0.0-win-x64.zip`. Распакуйте всю папку и запускайте `HardwareMonitor.exe`; DLL и папки рядом с ним необходимы. Нужны **Windows x64 и .NET Desktop Runtime 8 x64**. Это сборка без встроенного .NET, не установщик. PawnIO устанавливается отдельно. ZIP и `bin/Release` намеренно исключены из исходного Git.

Повторно подготовить сборку:

```powershell
dotnet publish HardwareMonitor/HardwareMonitor.csproj -c Release -r win-x64 --self-contained false -o .artifacts/release/HardwareMonitor-v1.0.0-win-x64 -p:Version=1.0.0 -p:DebugType=None -p:DebugSymbols=false -warnaserror
```

При новом архивировании сохраните `ПРОЧИТАТЬ.txt`, `THIRD_PARTY.md` и папку `Licenses` подготовленного пакета.

После проверки на втором ПК: **Releases → Draft a new release → Choose a tag → Create new tag v1.0.0**, целевая ветка `main`. Название `Мониторинг ПК 1.0.0`; краткое описание возьмите из CHANGELOG. Прикрепите ZIP, укажите Runtime 8 x64 и отдельную установку PawnIO. Сначала можно сохранить черновик. Публикация Release и смена видимости выполняются владельцем, автоматически ничего не отправлялось.
