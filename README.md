# ACadHotKey
Autocad перехват клавиш, для настоящих горячих клавиш

Модуль для AutoCAD 2020–2024 (C#, AutoCAD .NET API). Он запускает команды одиночными клавишами (`1` → `LINE`,
`2` → `PLINE`, …) без Enter. Клавиши перехватываются только когда фокус в области рисования. Если в этот момент
выполняется команда, она прерывается, и запускается команда, назначенная клавише.

* [Исследование: как перехватить клавиши и почему C#](docs/research.md)
* [Техническое задание](docs/technical-specification.md)

> Реализация ТЗ на C#. Автоматические проверки и сборка выполняются без AutoCAD.
> Приёмка внутри AutoCAD 2020–2024 ещё не выполнена: нужен Windows с установленным AutoCAD.
> [Матрица требований и результаты](docs/implementation-status.md), [протокол приёмки](docs/host-verification.md).

## Структура

| Путь | Содержимое |
|---|---|
| `src/ACadHotKey.Core` | Логика решений и разбор конфигурации. Не зависит от AutoCAD. |
| `src/ACadHotKey.Plugin` | Модуль AutoCAD `ACadHotKey.dll`: `Application.PreTranslateMessage`, команды `ACHK_*`. |
| `tests/ACadHotKey.Core.Tests` | Модульные тесты (xUnit). |
| `config/ACadHotKey.config.xml` | Пример назначений клавиш. |
| `bundle/ACadHotKey.bundle` | Пакет автозагрузки (`PackageContents.xml`). |
| `experiments/` | Проверка API, сборка против версий 2020–2024, упаковка и проверка комплекта установки. |

## Сборка и тесты

Нужен .NET SDK 8 или новее. Ссылки на AutoCAD берутся из NuGet (`AutoCAD.NET`), сам AutoCAD не нужен.

```bash
dotnet build ACadHotKey.sln -c Release -m:1
dotnet test tests/ACadHotKey.Core.Tests -c Release -m:1 --blame-hang-timeout 60s
# Bash + Python 3 (в Windows — Git Bash):
bash experiments/build-against-all-autocad-versions.sh
bash experiments/package.sh
bash experiments/test-package.sh
```

Пакет создаётся в `artifacts/ACadHotKey.bundle`. В него включены две сборки по
[таблице совместимости Autodesk](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-Customization/files/GUID-A6C680F2-DE2E-418A-A182-E4884073338A.htm):

| AutoCAD | API / Framework | Папка пакета |
|---|---|---|
| 2020 | 23.1 / .NET Framework 4.7 | `Contents/2020` |
| 2021–2024 | 24.0 / .NET Framework 4.8 | `Contents/2021-2024` |

В каждой папке: `ACadHotKey.dll`, **`ACadHotKey.Core.dll`**, `ACadHotKey.config.xml`.
Сборки Autodesk не распространяются: их предоставляет AutoCAD. Логи сборок и тестов — `experiments/logs/`.

## Установка и работа

1. Собрать пакет командой `bash experiments/package.sh`.
2. Скопировать **готовую** папку `artifacts/ACadHotKey.bundle` целиком в
   `%APPDATA%\Autodesk\ApplicationPlugins\` и перезапустить AutoCAD. `PackageContents.xml` выбирает
   подходящую сборку автоматически, в том числе в вертикальных продуктах.
3. Для разовой загрузки: `NETLOAD` → `ACadHotKey.dll` из папки своей версии; обе DLL и XML должны
   оставаться рядом. Если `SECURELOAD` блокирует загрузку, добавить папку модуля в `TRUSTEDPATHS`.
4. В чертеже нажать `1`: начинается `LINE`. Нажать `3` во время команды: она отменяется, начинается `CIRCLE`.
   Командная строка, поля палитр и текстовые редакторы принимают клавиши как обычно.
5. Для своих назначений скопировать XML в `%APPDATA%\ACadHotKey\ACadHotKey.config.xml`, изменить
   `key`/`macro` и выполнить `ACHK_RELOAD`. Синтаксис — [ТЗ, раздел 5](docs/technical-specification.md).
   Пример для своей .NET-команды: `<HotKey key="Ctrl+1" macro="MYCOMMAND" />`;
   для загруженной LISP-функции: `<HotKey key="Ctrl+2" macro="(c:MyFunc)" />`.

По умолчанию цифра в начале запроса прерывает команду. Для чисел использовать цифровой блок с NumLock,
начинать ввод с `@`/`-`/`.` или выключать перехват командой `ACHK_OFF`. После начатого ввода цифры
передаются в запрос до Enter/пробела/Esc или границы запроса (включая выбор мышью).
`passThroughOnPrompts` зависит от событий AutoCAD; для встроенных команд это проверяется по M9.
`cancelSequence="^C^C"` отменяет команду как Esc Esc; вариант с Enter — `cancelSequence="\n"`.
Сочетания с Ctrl и функциональные клавиши остаются горячими клавишами во время набора.

Ошибка XML сохраняет прежние назначения; сообщение содержит файл и причину. `enabled` задаёт начальное
состояние при загрузке/перезагрузке, а `ACHK_ON`/`OFF` меняют его до следующей загрузки.
Для удаления пакета закрыть AutoCAD и удалить папку `ACadHotKey.bundle`.

## Команды

| Команда | Действие |
|---|---|
| `ACHK_ON`, `ACHK_OFF`, `ACHK_TOGGLE` | Включить, выключить или переключить горячие клавиши |
| `ACHK_RELOAD` | Перечитать конфигурацию |
| `ACHK_LIST` | Список назначений |
| `ACHK_DIAG` | Диагностический журнал `%TEMP%\ACadHotKey.log` |

Журнал выключен по умолчанию. Для диагностики включить `ACHK_DIAG`, воспроизвести нажатие, выключить
`ACHK_DIAG`. Файл содержит целевое окно, класс, родителей, `CMDNAMES`, `CMDACTIVE`, запрос и решение.
Также можно задать `ACADHOTKEY_TRACE=1` до запуска AutoCAD.

В репозитории нет GitHub Actions. Рекомендации по CI:
https://github.com/link-assistant/hive-mind/blob/main/docs/CI-CD-BEST-PRACTICES.md — добавление остаётся решением владельца.
