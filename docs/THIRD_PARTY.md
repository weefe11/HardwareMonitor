# Используемые библиотеки

Зависимости восстанавливаются из NuGet. Их исходники не копируются в этот репозиторий и не изменены. Лицензия собственного кода пока не выбрана.

| Компонент готовой сборки | Версия | Лицензия / источник |
| --- | --- | --- |
| LibreHardwareMonitorLib | 0.9.6 | [MPL-2.0 и исходники](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) |
| BlackSharp.Core | 1.0.7 | [MPL-2.0, пакет и исходники](https://www.nuget.org/packages/BlackSharp.Core/1.0.7) |
| DiskInfoToolkit | 1.1.2 | [MPL-2.0, пакет и исходники](https://www.nuget.org/packages/DiskInfoToolkit/1.1.2) |
| RAMSPDToolkit-NDD | 1.4.2 | [MPL-2.0, пакет и исходники](https://www.nuget.org/packages/RAMSPDToolkit-NDD/1.4.2) |
| CommunityToolkit.Mvvm | 8.4.2 | [MIT](https://www.nuget.org/packages/CommunityToolkit.Mvvm/8.4.2) |
| WPF-UI и WPF-UI.Abstractions | 4.3.0 | [MIT](https://www.nuget.org/packages/WPF-UI/4.3.0) |
| HidSharp | 2.6.4 | [Текст лицензии в пакете](https://www.nuget.org/packages/HidSharp/2.6.4) |
| Mono.Posix.NETStandard и нативные вспомогательные библиотеки | 1.0.0 | [Лицензия издателя](https://go.microsoft.com/fwlink/?linkid=869050) |
| System.CodeDom, System.Management | 10.0.2 | [MIT, .NET](https://github.com/dotnet/runtime) |
| System.IO.Ports, System.Threading.AccessControl | 10.0.3 | [MIT, .NET](https://github.com/dotnet/runtime) |

В подготовленном ZIP сохранены доступные тексты лицензий и уведомления издателей в `Licenses/`. Для MPL-компонентов выше указаны ссылки на пакеты и соответствующие исходники. PawnIO не включён в архив и устанавливается отдельно.

Только для разработки/тестов: NUnit 4.4.0 (MIT), NUnit3TestAdapter 5.0.0 (MIT), Microsoft.NET.Test.Sdk 17.14.1 (MIT). Тестовый проект не входит в пакет приложения.
