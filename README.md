# ACadHotKey
Autocad перехват клавиш, для настоящих горячих клавиш

Модуль для AutoCAD 2020–2024 (C#, AutoCAD .NET API). Он запускает команды одиночными клавишами (`1` → `LINE`,
`2` → `PLINE`, …) без Enter. Клавиши перехватываются только когда фокус в области рисования. Если в этот момент
выполняется команда, она прерывается, и запускается команда, назначенная клавише.

* [Исследование: как перехватить клавиши и почему C#](docs/research.md)
* [Техническое задание](docs/technical-specification.md)

> Статус: прототип. Логика покрыта модульными тестами, модуль собирается против API AutoCAD 2020–2024.
> Поведение в самом AutoCAD нужно проверить по чек-листу из ТЗ (раздел 8.B).

## Структура

| Путь | Содержимое |
|---|---|
| `src/ACadHotKey.Core` | Логика решений и разбор конфигурации. Не зависит от AutoCAD. |
| `src/ACadHotKey.Plugin` | Модуль AutoCAD `ACadHotKey.dll`: `Application.PreTranslateMessage`, команды `ACHK_*`. |
| `tests/ACadHotKey.Core.Tests` | Модульные тесты (xUnit). |
| `config/ACadHotKey.config.xml` | Пример назначений клавиш. |
| `bundle/ACadHotKey.bundle` | Пакет автозагрузки (`PackageContents.xml`). |
| `experiments/` | Проверка API по сборкам AutoCAD, сборка против всех версий 2020–2024. |

## Сборка и тесты

Нужен .NET SDK 8 или новее. Ссылки на AutoCAD берутся из NuGet (`AutoCAD.NET`), сам AutoCAD не нужен.

```bash
dotnet build ACadHotKey.sln -c Release      # src/ACadHotKey.Plugin/bin/Release/net47/ACadHotKey.dll
dotnet test ACadHotKey.sln                  # модульные тесты
experiments/build-against-all-autocad-versions.sh   # сборка против API 2020, 2021, 2022, 2023, 2024
```

## Установка

* Разово: в AutoCAD команда `NETLOAD` → `ACadHotKey.dll`. `ACadHotKey.config.xml` лежит рядом с DLL.
* Автозагрузка: скопировать `ACadHotKey.dll` и `ACadHotKey.config.xml` в `bundle/ACadHotKey.bundle/Contents/`, затем
  папку `ACadHotKey.bundle` — в `%APPDATA%\Autodesk\ApplicationPlugins\`.
* Свои назначения: `%APPDATA%\ACadHotKey\ACadHotKey.config.xml`. Формат описан в ТЗ, раздел 5.

## Команды

| Команда | Действие |
|---|---|
| `ACHK_ON`, `ACHK_OFF`, `ACHK_TOGGLE` | Включить, выключить или переключить горячие клавиши |
| `ACHK_RELOAD` | Перечитать конфигурацию |
| `ACHK_LIST` | Список назначений |
| `ACHK_DIAG` | Диагностический журнал `%TEMP%\ACadHotKey.log` |
