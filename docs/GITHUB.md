# Поддержка и выпуск версий

Репозиторий: https://github.com/weefe11/HardwareMonitor. Основная ветка — `main`, решение — `HardwareMonitor.sln`.

Текущий стабильный релиз: **v1.0.0**. Репозиторий публичный; готовые сборки публикуются в [GitHub Releases](https://github.com/weefe11/HardwareMonitor/releases).

## Получить исходники и проверить

Нужны Windows, Git for Windows и .NET SDK 8 или новее.

```powershell
git clone https://github.com/weefe11/HardwareMonitor.git
cd HardwareMonitor
dotnet restore
dotnet build -c Release --no-restore -warnaserror
dotnet test -c Release --no-build --no-restore
& ./HardwareMonitor/bin/Release/net8.0-windows/HardwareMonitor.exe
```

Если clone уже есть: сначала `git status`, затем `git pull --ff-only` при чистом рабочем дереве. Не переносите старые `bin`, `obj` и локальные настройки между машинами.

Для низкоуровневых аппаратных датчиков при необходимости установите отдельно подписанный [PawnIO](https://pawnio.eu/) и подтвердите UAC при запуске приложения. После установки драйвера перезапустите HardwareMonitor.

[Проверенное оборудование](TESTED_HARDWARE.md) · [Состав репозитория](REPOSITORY_MANIFEST.md).

## Проверить GitHub Actions

Workflow **Windows CI** запускается при push, pull request и вручную.

Он выполняет:

`restore → Release build с -warnaserror → NUnit tests`

Для каждого нового коммита проверяйте отдельный результат во вкладке **Actions**. Успех предыдущего запуска не подтверждает новый commit.

CI использует моделируемые аппаратные данные и WPF-проверки и не заменяет ручную проверку на реальном железе. Результаты тестов сохраняются в артефакте `windows-test-results`.

## Внести изменение

Перед сборкой закройте приложение через «Выход» в трее.

```powershell
git status
git pull --ff-only
# Внесите изменение.
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
git add <изменённые-файлы>
git commit -m "Describe the change"
git push origin main
```

На новом компьютере при необходимости настройте автора локально для репозитория:

```powershell
git config user.name "Your Name"
git config user.email "YOUR_GITHUB_NOREPLY"
```

Не используйте force push без отдельной необходимости. Локальные настройки, аппаратные отчёты, логи, `.artifacts`, `bin` и `obj` в исходный Git не добавляются.

## Готовая сборка

Для обычного запуска используйте ZIP приложения из [Releases](https://github.com/weefe11/HardwareMonitor/releases). Распакуйте архив полностью и запускайте `HardwareMonitor.exe`.

Требования к готовой сборке:

- Windows x64;
- .NET Desktop Runtime 8 x64;
- PawnIO устанавливается отдельно, если нужен низкоуровневый доступ к датчикам;
- соседние DLL и ресурсы из ZIP должны оставаться рядом с EXE.

Воспроизводимая публикация:

```powershell
dotnet publish HardwareMonitor/HardwareMonitor.csproj -c Release -r win-x64 --self-contained false -o .artifacts/release/HardwareMonitor -p:DebugType=None -p:DebugSymbols=false -warnaserror
```

Перед архивированием проверьте, что в пакете сохранены пользовательские инструкции, сведения о сторонних компонентах и их лицензии. Тестовый проект, PDB и локальные отчёты в пользовательский ZIP не включаются.

## Выпуск новой версии

Для следующего стабильного выпуска:

1. Убедитесь, что `main` содержит только нужные изменения и рабочее дерево чистое.
2. Выполните clean/restore, Release-сборку с `-warnaserror` и весь test suite.
3. Проверьте новый commit во вкладке **Actions** — workflow должен завершиться успешно.
4. Если изменение связано с аппаратной совместимостью, повторите ручную проверку на подходящем реальном железе.
5. Обновите `CHANGELOG.md`, а при изменении подтверждённой совместимости — `TESTED_HARDWARE.md`.
6. Соберите новый ZIP и файл SHA-256.
7. Создайте новый тег вида `vX.Y.Z` на проверенном commit ветки `main`.
8. Создайте GitHub Release, приложите ZIP и SHA-256 и укажите реальные требования/ограничения.

Не перемещайте уже опубликованные стабильные теги на другой commit.

## Текущий релиз v1.0.0

Стабильный **v1.0.0** опубликован и проверен на двух физических конфигурациях:

- Intel Core i3-14100F + NVIDIA GeForce RTX 5060;
- AMD Ryzen 7 7840HS + AMD Radeon 780M Graphics.

Также выполнены NUnit-тесты и Windows CI. Подробности, границы проверки и недоступные датчики перечислены в [TESTED_HARDWARE.md](TESTED_HARDWARE.md).

## Лицензирование

Собственная лицензия проекта пока не выбрана. Публичная видимость репозитория сама по себе не даёт разрешения на свободное использование, изменение или распространение исходного кода.

Лицензии сторонних компонентов перечислены в [THIRD_PARTY.md](THIRD_PARTY.md).
