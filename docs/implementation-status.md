# Реализация issue #1

Основание: [issue #1](https://github.com/veb86/ACadHotKey/issues/1), включая комментарий владельца
от 17.11.2026: «Выполни техническое задание. Язык программирования C#».
Связанных обязательных sub-issues и замечаний в PR #3 на момент начала работы нет.

## План и проверка

| Требования | Работа | Доказательство |
|---|---|---|
| F1 | `HotKeyPlugin.OnPreTranslateMessage`, WM_KEYDOWN / WM_SYSKEYDOWN | Компиляция с пятью API; реальная доставка сообщений — M1–M4, Q4, **блокировано** |
| F2 | `FocusPolicy`: GetFocus, цепочка родителей, запреты, модальные окна и передний план | `Focus_requires_actual_focused_document_and_checks_control_ancestors`, `Cyclic_or_unrelated_window_ancestry_is_rejected`; реальные окна — M5–M7, **блокировано** |
| F3 | `KeyMessageProcessor` отправляет до `Handled`; движок отслеживает отпускание | `Keydown_repeat_release_queues_exactly_one_cancelled_macro`, `Failed_dispatch_passes_original_key_and_does_not_swallow_release`; подавление символов — M1, M16, **блокировано** |
| F4 | Снимок CMDNAMES / CMDACTIVE в модуле | `Lisp_only_activity_counts_as_active_command`, `Active_command_is_interrupted_with_cancel_sequence_then_macro_runs`; реальные переменные — M2–M4 |
| F5 | Запуск через SendStringToExecute с echoMacro | `Idle_hotkey_runs_macro_without_cancel_prefix`, `MacroTextTests`; выполнение — M1, **блокировано** |
| F6 | Interrupt / PassThrough / Transparent и настраиваемая отмена | `PassThrough_policy_types_the_key_while_command_is_active_but_runs_when_idle`, `Transparent_policy_runs_command_without_cancelling`, `Custom_cancel_sequence_is_used`; M2–M4, M10, **блокировано** |
| F7 | Отдельные NumPad, учёт набора, PromptTracker с границами и вложенными командами | `Top_row_digit_and_numpad_digit_are_different_keys`, `After_typing_at_prompt_digits_are_input_until_enter`, `Prompt_completion_by_mouse_resets_typing`, `Transparent_command_restores_outer_prompt`; встроенные запросы — M8–M9, Q3, **блокировано** |
| F8 | ExcludedCommands и запрещённые классы окон/родителей | `Text_editing_commands_disable_hotkeys`, тесты FocusPolicy; M7, **блокировано** |
| F9 | IgnoreDuringScript и бит CMDACTIVE 4 | `Script_disables_hotkeys_unless_configured` |
| F10 | Автоповтор проверяется до назначения, модификаторов, фокуса и выключателя | `Held_hotkey_stays_swallowed_after_context_changes`, `Auto_repeat_of_passed_key_is_passed`, `Passed_repeat_does_not_become_hotkey_after_modifiers_change`; M11, **блокировано** |
| F11 | Строгий XML и атомарный HotKeyConfigStore | `HotKeyConfigTests`, `Reload_is_atomic_and_does_not_fall_back_from_invalid_user_config`; M13 |
| F12 | ACHK_ON/OFF/TOGGLE/RELOAD/LIST/DIAG, вывод исключений запросов, журнал | Проверка обработчиков; `Runtime_on_can_enable_a_configuration_that_started_disabled`, `Reload_resets_typing_and_applies_configured_enabled_state`; M12/M17, **блокировано** |
| F13 | Виртуальные коды Windows, точные модификаторы | `KeyChordTests`, `Non_hotkey_and_different_modifiers_pass_through`; раскладка — M15, **блокировано** |
| N1 | Быстрый выход для остальных сообщений, словарь назначений, журнал без I/O при выключении | Проверка исходников; `Binding_index_tracks_replacement_removal_and_clear`; задержка в AutoCAD не измерена |
| N2 | Исключения отправки/журнала не выходят в цикл; рабочий XML сохраняется | `Failed_dispatch_passes_original_key_and_does_not_swallow_release`, `Logger_failure_cannot_escape_dispatch`, тест атомарной загрузки |
| N3 | Две сборки, диапазоны SDK по документации Autodesk | Матрица сборок API 2020–2024, verify-package.py; загрузка в продукт — **блокировано** |
| N4 | Независимая Core и реальные подписки PromptTracker с малыми подменами событий | 115 автоматических тестов; тесты не подменяют приёмку в AutoCAD |
| N5 | Журнал выключен, если нет ACHK_DIAG / ACADHOTKEY_TRACE=1 | Проверка TraceLog и условных вызовов |
| N6 | dotnet CLI / NuGet без установленного AutoCAD | Сборки в Linux; README содержит команды |
| Раздел 11 | Пакет включает обе DLL и XML для каждого поколения; инструкция установки/удаления | package.sh / verify-package.py; автозагрузка в AutoCAD — **блокировано** |
| Раздел 8.B, M1–M17 | Проверка внутри каждого AutoCAD 2020–2024 | Требуется Windows с установленным AutoCAD; результаты не подменяются модульными тестами |
| PR | Атомарные коммиты, актуальная основная ветка, описание и чистая рабочая копия | Проверка diff, статуса Git и проверок GitHub |

## Принятые решения

* Q1: цифра до начала ввода прерывает команду; числовой блок, начатый ввод и `passThroughOnPrompts`
  позволяют вводить числа, как предусмотрено ТЗ.
* Q2: прерывание означает Esc Esc (`^C^C`). Enter настраивается через `cancelSequence`, поскольку
  правая кнопка зависит от настроек AutoCAD и не всегда прерывает команду.
* Q5–Q6: один XML на пользователя, без графического редактора. AutoCAD 2025+ остаётся опциональным
  пунктом исходного ТЗ, вне диапазона issue (2020–2024).
* Совместимость проверяется по [таблице Autodesk](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-Customization/files/GUID-A6C680F2-DE2E-418A-A182-E4884073338A.htm):
  для 2020 используется SDK 23.1 / net47, для 2021–2024 — SDK 24.0 / net48.
  Компиляция с каждой версией API сама по себе не доказывает загрузку одной DLL во все версии.

## Ограничение среды

Среда разработки — Linux, .NET SDK 10.0.112; Windows, AutoCAD и его лицензия здесь отсутствуют.
M1–M17, Q3 (события запросов встроенных команд) и Q4 (доставка сообщений при командах/jig)
пока **не проверены**. Для них требуется реальный AutoCAD; PR не должен считаться полностью
принятым до получения протокола по всем пяти версиям. Скриншоты интерфейса без AutoCAD не создаются.

## Автоматическая проверка

* До исправления: 14 из 23 выбранных регрессионных сценариев не прошли (остальные 9 прошли).
  Ошибки: утечка автоповторов, включение/перезагрузка, привязанный пробел, модальный диалог и принятие
  неверных значений XML. Локальный журнал: `experiments/logs/regressions-before.log`.
* После исправления: 115 тестов, 0 ошибок, 0 пропусков; включены проверки отправки с отказом хоста,
  всех 11 типов запросов, снятия подписок, вложенных прозрачных команд и макросов.
* Сборки исходников: AutoCAD.NET 23.1.0, 24.0.0, 24.1.51000, 24.2.0, 24.3.0.
* Упаковка: отдельные сборки SDK 23.1 и 24.0; проверка ровно одного подходящего компонента для каждой
  версии, наличия двух DLL и XML, отсутствия эталонных DLL Autodesk. `test-package.sh` проверяет,
  что удаление ACadHotKey.Core.dll и добавление лишней DLL делают проверку неуспешной.
* В репозитории нет CI. В README приведена рекомендация по добавлению CI; новые workflows не добавлялись.

Воспроизведение: команды README; локальные журналы и TRX сохраняются в `experiments/logs/`
(исключены из Git, поскольку содержат абсолютные пути и повторяемый вывод сборки).
