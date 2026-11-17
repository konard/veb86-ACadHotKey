# Исследование: перехват горячих клавиш в AutoCAD 2020–2024

Документ отвечает на вопросы из [issue #1](https://github.com/veb86/ACadHotKey/issues/1):

1. Можно ли в AutoCAD 2020–2024 перехватить нажатие клавиш (например, `1`, `2`, `3`), которые сейчас сразу печатаются в командную строку, и вместо этого запустить свою функцию?
2. Как сделать, чтобы перехват работал **только когда фокус в чертеже**?
3. Как понять, **выполняется ли сейчас команда**, и если да — **прервать её** и запустить команду, назначенную клавише?
4. На каком языке (C# или C++) это проще реализовать?

Короткий ответ: **да, это возможно, и проще всего на C#** (AutoCAD .NET API). В API есть событие
`Application.PreTranslateMessage`. Оно получает каждое оконное сообщение AutoCAD **до** того, как AutoCAD его
обработает, и позволяет «съесть» сообщение (`e.Handled = true`). Активную команду показывают системные
переменные `CMDNAMES`/`CMDACTIVE`. Прервать команду и запустить новую можно строкой
`"^C^C" + команда` через `Document.SendStringToExecute`. Требования к языку, обоснование и риски — ниже.
Техническое задание — в [technical-specification.md](technical-specification.md).
Реализация (C#) — в [`src/`](../src); фактическая приёмка в AutoCAD пока не выполнена.

---

## 1. Почему цифра сразу уходит в командную строку

Когда фокус находится в области рисования, AutoCAD передаёт печатные символы в командную строку (или в
подсказку динамического ввода). Штатные способы ускорить ввод команд этого не меняют:

| Штатный механизм | Почему не подходит |
|---|---|
| Псевдонимы команд (`acad.pgp`, например `L` → `LINE`) | Нужно нажать Enter или пробел после псевдонима. |
| Сочетания клавиш CUI (Keyboard Shortcuts) | Рассчитаны на сочетания с модификаторами (Ctrl, Shift, Alt) и на функциональные клавиши. Одиночная цифра или буква без модификатора в AutoCAD — это ввод текста. |
| Макросы кнопок (`^C^C_LINE`) | Запускаются мышью, а не одиночной клавишей. |

Поэтому нужен модуль, который увидит сообщение клавиатуры **раньше** командной строки.

## 2. Способы перехвата

### 2.1. `Application.PreTranslateMessage` (AutoCAD .NET API) — рекомендуемый

Событие объявлено в `AcCoreMgd.dll`. Это проверено по эталонным сборкам из официальных NuGet-пакетов
`AutoCAD.NET` 23.1.0 (AutoCAD 2020) и 24.3.0 (AutoCAD 2024) экспериментом
[`experiments/ApiInspector`](../experiments/ApiInspector):

```text
Autodesk.AutoCAD.ApplicationServices.Core.Application
   Event PreTranslateMessageEventHandler PreTranslateMessage        // "Occurs before a message is translated."
Autodesk.AutoCAD.ApplicationServices.PreTranslateMessageEventArgs
   Property System.Windows.Interop.MSG Message {get;set;}
   Property System.Boolean Handled {get;set;}
```

* Обработчик вызывается в главном (UI) потоке AutoCAD для каждого сообщения цикла сообщений. Сообщения
  `WM_KEYDOWN`/`WM_KEYUP` приходят с `hwnd` окна, у которого сейчас фокус клавиатуры.
* Если обработчик ставит `Handled = true` для `WM_KEYDOWN`, AutoCAD не выполняет `TranslateMessage`/`DispatchMessage`.
  Символ не появляется ни в командной строке, ни в динамическом вводе.
* Это управляемая обёртка над функцией ObjectARX `acedRegisterFilterWinMsg`.
* Такой подход описан в документации Autodesk ([Handle Application Events (.NET)][autodesk-app-events]).
  Его применяют на практике: Kean Walmsley (Autodesk) в AutoCAD 2015 перехватывал `WM_KEYDOWN`/`WM_CHAR`,
  ставил `e.Handled = true` и запускал команду через `SendStringToExecute`. Он отмечает, что `Editor.Command()`
  в этом контексте не работает ([keanw.com, 2015][kean-global-kw]). Другой пример — обработка клавиши Delete
  ([Drive AutoCAD with Code, 2022][norman-2022]).

### 2.2. ObjectARX (C++): `acedRegisterFilterWinMsg`

Тот же механизм на уровне C++: функция-фильтр `bool filter(MSG*)` вызывается до обработки сообщения AutoCAD.
Возможности те же, что у варианта 2.1, но:

* ObjectARX-модуль (`.arx`) бинарно привязан к версии AutoCAD. Для 2020 (R23.1) и для 2021–2024 (R24.x)
  нужны **две разные сборки** с разными ObjectARX SDK и наборами инструментов Visual Studio.
* Нужно больше кода: MFC/Win32, ручное управление памятью, регистрация команд через `acedRegCmds`.
  Ошибка в C++-фильтре роняет весь AutoCAD.
* Выигрыша в скорости нет: фильтр выполняет несколько сравнений чисел.

### 2.3. Хуки Windows и внешние программы (AutoHotkey и т. п.)

* **Глобальный хук** `WH_KEYBOARD_LL` или AutoHotkey работает в отдельном процессе. Он не знает,
  выполняется ли команда, и не может надёжно это узнать: COM-вызовы к AutoCAD во время команды часто
  отклоняются («Call was rejected by callee»). Отличить область рисования от палитр и полей ввода можно только
  по классам окон. Такой хук ловит клавиши и в других программах, поэтому вариант **не рекомендуется**.
* **Хук потока** `SetWindowsHookEx(WH_GETMESSAGE, …)` внутри процесса AutoCAD технически возможен, но
  повторяет `PreTranslateMessage`, только сложнее и с риском конфликта с самим AutoCAD.

### 2.4. AutoLISP

У AutoLISP нет событий клавиатуры: `grread` работает только внутри уже запущенного цикла. Вариант
**не подходит**. LISP-функции можно **вызывать** из горячих клавиш (макрос `(c:MyFunc)`).

## 3. Как понять, что фокус в чертеже

Сообщение клавиатуры адресовано окну с фокусом (`MSG.hwnd`). Правило:

1. `Document.Window.Handle` — окно активного документа (свойство `Document.Window` есть в API 2020–2024).
2. `MSG.hwnd` должен совпадать с `GetFocus()` и иметь цепочку родителей до этого окна.
   Командная строка, палитры (Свойства, Слои), лента и диалоги — это **другие** окна, поэтому клавиши в них
   работают как обычно.
3. Исключается вся цепочка окон текстового ввода внутри чертежа (класс окна или родителя содержит `Edit` или `RichEdit`),
   например встроенный редактор MTEXT. Кроме того, пока выполняются команды редактирования текста (`MTEXT`,
   `TEXT`, `DDEDIT`, `TEXTEDIT`, `ATTIPEDIT`, …), все клавиши передаются AutoCAD.

Также перехват выключается в модальных диалогах и вне переднего плана AutoCAD.
Точные классы окон в 2020–2024 нужно снять командой `ACHK_DIAG`. Она пишет в журнал
`hwnd`, класс окна и цепочку родителей для каждого нажатия. Список исключаемых классов задаётся в конфигурации.

## 4. Как понять, выполняется ли команда

| Источник | Что даёт |
|---|---|
| Системная переменная `CMDNAMES` | Имена активных команд, включая прозрачные: `LINE'ZOOM`. Пусто — команды нет. |
| Системная переменная `CMDACTIVE` | Битовый код: 1 — обычная команда, 2 — прозрачная, 4 — сценарий, 8 — диалог, 16 — DDE, 32 — AutoLISP, 64 — команда ObjectARX ([Autodesk Help: CMDACTIVE][cmdactive]). |
| `Document.CommandInProgress`, `Editor.IsQuiescent` | То же через свойства API (проверено в эталонных сборках). |
| События `Document.CommandWillStart/CommandEnded/CommandCancelled/CommandFailed` | Моменты начала и конца команды. |
| События `Editor.PromptingForPoint/Distance/Integer/String/…` | **Какой ввод** ждёт текущий запрос: точку, расстояние, число, строку… |

Чтение `CMDNAMES` и `CMDACTIVE` в обработчике `PreTranslateMessage` дешёвое, и его достаточно для правила
«команда идёт → прервать».

## 5. Как прервать команду и запустить нужную

```csharp
doc.SendStringToExecute("\x03\x03_LINE\n", true, false, true);
```

* `\x03` (Ctrl+C) — отмена, как Esc. Двойной `^C^C` — стандартный префикс макросов CUI: он выходит и из
  вложенных запросов, и из прозрачных команд.
* `SendStringToExecute` ставит строку во входную очередь документа. Её можно вызывать из контекста приложения,
  то есть из обработчика `PreTranslateMessage`.
* В issue сказано «эффект правой кнопки мыши». Правая кнопка в AutoCAD работает как Enter или открывает
  контекстное меню, в зависимости от `SHORTCUTMENU`/настроек. Enter **завершает** текущий запрос (например,
  LINE заканчивается), но в некоторых запросах Enter принимает значение по умолчанию, и команда продолжается.
  Однозначно прерывает только Esc (`^C`), поэтому по умолчанию используется `^C^C`. Префикс настраивается
  (`cancelSequence`).
* Альтернатива без прерывания — прозрачный вызов `'ZOOM` для команд, которые это допускают (`Transparent`).

## 6. Главный конфликт: цифры как горячие клавиши и цифры как ввод

Если `1` — горячая клавиша, то при запросе «Длина:» пользователь не сможет набрать `100` с основной клавиатуры:
первая же `1` прервёт команду. Это следует прямо из требования «прерывать команду». Решения (все реализованы
в прототипе и настраиваются):

1. **Цифровой блок клавиатуры.** `1` в верхнем ряду (VK `0x31`) и `Num1` (VK `0x61`) — разные клавиши.
   Горячие клавиши на верхнем ряду, числа набираются на цифровом блоке. Вариант без дополнительных условий.
2. **«Пользователь уже печатает».** Если в текущем запросе уже введён символ (например, `@`), то символьные
   горячие клавиши передаются как ввод до Enter, пробела или Esc (`passThroughWhileTyping`).
3. **По типу запроса.** Для клавиши можно указать запросы, где она остаётся вводом:
   `passThroughOnPrompts="Distance Double Integer"`. **Нужно проверить**, генерирует ли AutoCAD события
   `PromptingForXxx` для запросов встроенных команд. Документация это не уточняет; проверка — через `ACHK_DIAG`.
4. **Поведение для клавиши:** `whenCommandActive="PassThrough"` — во время команды клавиша работает как обычный ввод.
5. **Быстрое выключение:** команды `ACHK_OFF`/`ACHK_ON`/`ACHK_TOGGLE` (`ACHK_TOGGLE` можно назначить на Ctrl-сочетание в CUI).

## 7. C# или C++: сравнение

| Критерий | C# (.NET API) | C++ (ObjectARX) |
|---|---|---|
| Перехват клавиш | `Application.PreTranslateMessage` + `e.Handled` | `acedRegisterFilterWinMsg` |
| Состояние команды, события запросов | Есть (`CMDNAMES`, `CommandWillStart`, `Editor.PromptingForXxx`) | Есть (`acedGetVar`, `AcEditorReactor`) |
| Запуск или прерывание команды | `Document.SendStringToExecute` | `acDocManager->sendStringToExecute` |
| Сборки для 2020–2024 | Две: SDK 23.1 / `net47` (2020) и SDK 24.0 / `net48` (2021–2024), по [таблице Autodesk](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-Customization/files/GUID-A6C680F2-DE2E-418A-A182-E4884073338A.htm). Компиляция одних исходников с разными API не доказывает бинарную совместимость одной DLL. | Минимум две: R23.1 (2020) и R24 (2021–2024). |
| Инструменты | Visual Studio любой редакции или `dotnet` CLI; ссылки на AutoCAD — из NuGet | Visual Studio с нужным набором инструментов, ObjectARX SDK для каждой версии |
| Тестирование логики без AutoCAD | Логика вынесена в `ACadHotKey.Core`; тесты запускаются на любой ОС | Сложнее |
| Риск уронить AutoCAD | Низкий: исключения перехватываются | Выше: ошибка памяти — аварийное завершение AutoCAD |
| Будущие версии | AutoCAD 2025+ работает на .NET 8: нужна отдельная цель сборки и проверка миграции | Новая сборка под каждую серию ObjectARX |

**Вывод: реализовывать на C#.** Нужные механизмы есть в .NET API. Пакет содержит две сборки и выбирает их
для AutoCAD 2020–2024 и вертикальных продуктов на их основе (Civil 3D, Architecture, MEP, Mechanical).
AutoCAD LT не поддерживается:
LT не загружает .NET/ObjectARX-модули.

## 8. Риски и что проверить в реальном AutoCAD

В этой среде разработки нет Windows и AutoCAD. Поэтому проверено, что код **компилируется** против API всех
версий 2020–2024 и что **логика** проходит модульные тесты. Поведение внутри AutoCAD нужно подтвердить по
чек-листу из ТЗ (раздел 8). Основные вопросы:

| # | Вопрос | Почему важен | Как проверить |
|---|---|---|---|
| V1 | Приходит ли `WM_KEYDOWN` в `PreTranslateMessage` **во время** команды (`LINE`, `MOVE`, jig) в 2020–2024? | Есть противоречивое сообщение на форуме Autodesk: «перестало работать с 2015» (об Esc во время команды), и ответ, что работает в 2013–2017 ([форум Autodesk][forum-esc]). У Kean (2015) перехват во время ввода работал. | `ACHK_DIAG`, затем `LINE`, затем нажать `1`; смотреть `%TEMP%\ACadHotKey.log`. |
| V2 | Не приходит ли `WM_CHAR` после «съеденного» `WM_KEYDOWN`? | Иначе цифра всё же напечатается. | Нажать `1` в простое: в командной строке не должно быть символа. Если символ появляется, «съедать» и `WM_CHAR`. |
| V3 | Какие классы окон у области рисования и у встроенных редакторов текста? | Правило фокуса из раздела 3. | Журнал `ACHK_DIAG`: поле `chain`. |
| V4 | Генерируются ли `Editor.PromptingForXxx` для встроенных команд? | Правило `passThroughOnPrompts`. | Журнал `ACHK_DIAG`: строки `prompt …`. |
| V5 | Как работает динамический ввод (`DYNMODE`)? | У Kean динамический ввод отключался. | Повторить V1 и V2 с `DYNMODE=1` и `DYNMODE=0`. |
| V6 | Alt-сочетания | После «съеденного» Alt+клавиша отпускание Alt может открыть подсказки ленты. | Рекомендуется не назначать Alt-сочетания. |

## 9. Источники

* Эталонные сборки и XML-документация AutoCAD .NET API: NuGet-пакеты [`AutoCAD.NET`](https://www.nuget.org/packages/AutoCAD.NET)
  23.1.0 (2020), 24.0.0 (2021), 24.1.51000 (2022), 24.2.0 (2023), 24.3.0 (2024), 25.0.0 (2025, `net8.0`).
* [Autodesk: Handle Application Events (.NET)][autodesk-app-events]
* [Autodesk: CMDACTIVE (System Variable)][cmdactive]
* [Kean Walmsley: Adding support for global keywords at AutoCAD’s command-line using .NET – Part 2 (2015)][kean-global-kw]
* [Norman Yuan: Assign a Custom Action to a Keyboard Key by Capturing Windows' Key-Press Message (2022)][norman-2022]
* [Autodesk Community: Capturing escape key during a command][forum-esc]
* Win32: [`IsChild`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-ischild),
  [`WM_KEYDOWN` (бит 30 lParam — автоповтор)](https://learn.microsoft.com/windows/win32/inputdev/wm-keydown),
  [коды виртуальных клавиш](https://learn.microsoft.com/windows/win32/inputdev/virtual-key-codes)

[autodesk-app-events]: https://help.autodesk.com/cloudhelp/2016/ENU/AutoCAD-NET/files/GUID-4BD5D384-5448-4D19-9023-DA12A55FAEF0.htm
[cmdactive]: https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-LT/files/GUID-18B2E090-6C94-4C24-A70A-F2D6BDD5569C.htm
[kean-global-kw]: https://keanw.com/2015/03/adding-support-for-global-keywords-at-autocads-command-line-using-net-part-2.html
[norman-2022]: https://drive-cad-with-code.blogspot.com/2022/05
[forum-esc]: https://forums.autodesk.com/t5/net-forum/capturing-escape-key-during-a-command/td-p/4701031
