using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACadHotKey.Core
{
    /// <summary>Win32 keyboard message identifiers (WinUser.h).</summary>
    public static class WindowMessages
    {
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_CHAR = 0x0102;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;
        public const int WM_SYSCHAR = 0x0106;

        public static bool IsKeyDown(int msg) => msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
        public static bool IsKeyUp(int msg) => msg == WM_KEYUP || msg == WM_SYSKEYUP;
        public static bool IsKeyboard(int msg) => msg >= WM_KEYDOWN && msg <= WM_SYSCHAR;

        /// <summary>Bit 30 of lParam of WM_KEYDOWN: the key was already down (auto-repeat).</summary>
        public static bool IsRepeat(long lParam) => (lParam & (1L << 30)) != 0;
    }

    /// <summary>A keyboard message as seen by Application.PreTranslateMessage.</summary>
    public readonly struct KeyboardMessage
    {
        public KeyboardMessage(int message, int virtualKey, KeyModifiers modifiers = KeyModifiers.None, bool isRepeat = false)
        {
            Message = message;
            VirtualKey = virtualKey;
            Modifiers = modifiers;
            IsRepeat = isRepeat;
        }

        public int Message { get; }
        public int VirtualKey { get; }
        public KeyModifiers Modifiers { get; }
        public bool IsRepeat { get; }

        public static KeyboardMessage KeyDown(int vk, KeyModifiers modifiers = KeyModifiers.None, bool isRepeat = false) =>
            new KeyboardMessage((modifiers & KeyModifiers.Alt) != 0 ? WindowMessages.WM_SYSKEYDOWN : WindowMessages.WM_KEYDOWN,
                vk, modifiers, isRepeat);

        public static KeyboardMessage KeyUp(int vk, KeyModifiers modifiers = KeyModifiers.None) =>
            new KeyboardMessage((modifiers & KeyModifiers.Alt) != 0 ? WindowMessages.WM_SYSKEYUP : WindowMessages.WM_KEYUP,
                vk, modifiers);
    }

    /// <summary>Snapshot of the AutoCAD state taken when a key message arrives.</summary>
    public sealed class EditorState
    {
        /// <summary>The key message targets the drawing area of the active document (see <see cref="FocusPolicy"/>).</summary>
        public bool DrawingHasFocus { get; set; }

        /// <summary>Value of the CMDNAMES system variable, e.g. "LINE" or "LINE'ZOOM". Empty when idle.</summary>
        public string CommandNames { get; set; } = string.Empty;

        /// <summary>Value of the CMDACTIVE system variable (bit code: 1 command, 2 transparent, 4 script, 8 dialog, 16 DDE, 32 AutoLISP, 64 ObjectARX).</summary>
        public int CmdActive { get; set; }

        /// <summary>Kind of the prompt AutoCAD is currently waiting on (if known).</summary>
        public PromptKind Prompt { get; set; }

        public const int CmdActiveCommand = 1;
        public const int CmdActiveTransparent = 2;
        public const int CmdActiveScript = 4;
        public const int CmdActiveDialog = 8;
        public const int CmdActiveLisp = 32;

        public bool IsCommandActive => CmdActive != 0 || !string.IsNullOrEmpty(CommandNames);

        /// <summary>Individual command names from CMDNAMES ("LINE'ZOOM" -> LINE, ZOOM).</summary>
        public IEnumerable<string> Commands =>
            (CommandNames ?? string.Empty).Split(new[] { '\'' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim().TrimStart('_', '.', '-', '+'));
    }

    public enum KeyAction
    {
        /// <summary>Let AutoCAD process the message normally.</summary>
        PassThrough,

        /// <summary>Eat the message (PreTranslateMessageEventArgs.Handled = true) and do nothing else.</summary>
        Swallow,

        /// <summary>Eat the message and send <see cref="KeyDecision.Text"/> via Document.SendStringToExecute.</summary>
        Execute,
    }

    public sealed class KeyDecision
    {
        private KeyDecision(KeyAction action, string reason, string? text = null, HotKeyBinding? binding = null)
        {
            Action = action;
            Reason = reason;
            Text = text;
            Binding = binding;
        }

        public KeyAction Action { get; }
        public string Reason { get; }
        public string? Text { get; }
        public HotKeyBinding? Binding { get; }

        /// <summary>True when the plugin must set PreTranslateMessageEventArgs.Handled.</summary>
        public bool Handled => Action != KeyAction.PassThrough;

        public static KeyDecision Pass(string reason) => new KeyDecision(KeyAction.PassThrough, reason);
        public static KeyDecision Swallow(string reason) => new KeyDecision(KeyAction.Swallow, reason);
        public static KeyDecision Execute(string text, HotKeyBinding binding, string reason) =>
            new KeyDecision(KeyAction.Execute, reason, text, binding);

        public override string ToString() =>
            Action == KeyAction.Execute ? $"{Action} {MacroText.Visualize(Text)} ({Reason})" : $"{Action} ({Reason})";
    }

    /// <summary>
    /// Decides for every keyboard message whether it is a hotkey and what to do with it.
    /// It contains all the rules of the specification (docs/technical-specification.md, section 4)
    /// and is independent of AutoCAD so it can be unit-tested.
    /// One instance per AutoCAD session; called on the AutoCAD UI thread only.
    /// </summary>
    public sealed class HotKeyEngine
    {
        private readonly HashSet<int> _swallowedKeyDowns = new HashSet<int>();
        private bool _typing;
        private string _lastCommandNames = string.Empty;

        public HotKeyEngine(HotKeyConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public HotKeyConfig Config { get; set; }

        /// <summary>Runtime on/off switch (commands ACHK_ON / ACHK_OFF), independent from the config file.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>True when the user has typed characters at the current prompt (see PassThroughWhileTyping).</summary>
        public bool IsTyping => _typing;

        /// <summary>Forget the typed-input state (call on CommandWillStart/Ended/Cancelled and document switch).</summary>
        public void ResetTyping() => _typing = false;

        public KeyDecision Process(KeyboardMessage message, EditorState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            var commandNames = state.CommandNames ?? string.Empty;
            if (!string.Equals(commandNames, _lastCommandNames, StringComparison.OrdinalIgnoreCase))
            {
                // A command started or finished since the last key: whatever was typed belongs to the old prompt.
                _typing = false;
                _lastCommandNames = commandNames;
            }

            if (WindowMessages.IsKeyUp(message.Message))
            {
                // Eat the key-up of a key whose key-down was eaten, so AutoCAD never sees half of a keystroke.
                return _swallowedKeyDowns.Remove(message.VirtualKey)
                    ? KeyDecision.Swallow("key-up of a handled hotkey")
                    : KeyDecision.Pass("key-up");
            }

            if (!WindowMessages.IsKeyDown(message.Message))
                return KeyDecision.Pass("not a key-down message");

            if (!Enabled || !Config.Enabled)
                return KeyDecision.Pass("hotkeys disabled");

            if (!state.DrawingHasFocus)
                return KeyDecision.Pass("focus is not in the drawing area");

            var binding = Config.Find(new KeyChord(message.VirtualKey, message.Modifiers));
            if (binding == null)
            {
                TrackTyping(message);
                return KeyDecision.Pass("not a hotkey");
            }

            if (message.IsRepeat)
            {
                // Holding a handled hotkey must neither start the command again nor type characters;
                // repeats of a key that was passed to AutoCAD are passed as well.
                return _swallowedKeyDowns.Contains(message.VirtualKey)
                    ? KeyDecision.Swallow("auto-repeat of a handled hotkey")
                    : KeyDecision.Pass("auto-repeat of a passed key");
            }

            var excluded = state.Commands.FirstOrDefault(c => Config.ExcludedCommands.Contains(c));
            if (excluded != null)
                return KeyDecision.Pass($"text input command {excluded} is active");

            if (Config.IgnoreDuringScript && (state.CmdActive & EditorState.CmdActiveScript) != 0)
                return KeyDecision.Pass("script is running");

            // Only character-producing hotkeys ("1", "Q") can be part of typed input; F-keys and Ctrl+X still work.
            if (Config.PassThroughWhileTyping && _typing && ProducesCharacter(message))
            {
                TrackTyping(message);
                return KeyDecision.Pass("user is typing at the prompt");
            }

            if (!state.IsCommandActive)
                return Run(binding, MacroText.Build(binding.Macro), "no command is active");

            if (state.Prompt != PromptKind.None && binding.PassThroughOnPrompts.Contains(state.Prompt))
            {
                TrackTyping(message);
                return KeyDecision.Pass($"prompt for {state.Prompt} accepts this key as input");
            }

            var policy = binding.WhenCommandActive ?? Config.DefaultWhenCommandActive;
            switch (policy)
            {
                case CommandActivePolicy.PassThrough:
                    TrackTyping(message);
                    return KeyDecision.Pass($"command {commandNames} is active, policy PassThrough");
                case CommandActivePolicy.Transparent:
                    return Run(binding, MacroText.BuildTransparent(binding.Macro),
                        $"command {commandNames} is active, policy Transparent");
                default:
                    return Run(binding, MacroText.Build(binding.Macro, Config.CancelSequence),
                        $"command {commandNames} is active, policy Interrupt");
            }
        }

        private KeyDecision Run(HotKeyBinding binding, string text, string reason)
        {
            _swallowedKeyDowns.Add(binding.Key.VirtualKey);
            _typing = false;
            return KeyDecision.Execute(text, binding, reason);
        }

        private void TrackTyping(KeyboardMessage message)
        {
            if (VirtualKeys.IsInputTerminator(message.VirtualKey))
            {
                _typing = false;
                return;
            }

            if (ProducesCharacter(message) && message.VirtualKey != VirtualKeys.Back)
            {
                _typing = true;
            }
        }

        private static bool ProducesCharacter(KeyboardMessage message) =>
            (message.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) == 0 && VirtualKeys.IsTypingKey(message.VirtualKey);
    }

    /// <summary>Decides whether the window that receives a key message is the drawing area.</summary>
    public static class FocusPolicy
    {
        /// <param name="isDocumentWindowOrChild">msg.hwnd is Document.Window.Handle of the active document or its descendant (Win32 IsChild).</param>
        /// <param name="windowClassName">Win32 class name of msg.hwnd (GetClassName).</param>
        /// <param name="deniedClassFragments">Class name fragments of text input controls (in-place editors, edit boxes).</param>
        public static bool IsDrawingArea(bool isDocumentWindowOrChild, string? windowClassName, IEnumerable<string> deniedClassFragments)
        {
            if (!isDocumentWindowOrChild) return false;
            var cls = windowClassName ?? string.Empty;
            return !deniedClassFragments.Any(f => f.Length > 0 && cls.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }

    /// <summary>Converts configured macros into the string passed to Document.SendStringToExecute.</summary>
    public static class MacroText
    {
        /// <summary>
        /// Expands CUI-like escapes: "^C" -> Ctrl+C (cancel, same as Esc), "^[" -> Esc character,
        /// "\n" (backslash + n) -> Enter. Other text is kept verbatim.
        /// </summary>
        public static string Expand(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text!.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '^' && i + 1 < text.Length && (text[i + 1] == 'C' || text[i + 1] == 'c'))
                {
                    sb.Append('\x03');
                    i++;
                }
                else if (c == '^' && i + 1 < text.Length && text[i + 1] == '[')
                {
                    sb.Append('\x1B');
                    i++;
                }
                else if (c == '\\' && i + 1 < text.Length && text[i + 1] == 'n')
                {
                    sb.Append('\n');
                    i++;
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        /// <summary>Cancel prefix (optional) + macro + Enter (unless the macro already ends with Enter/space).</summary>
        public static string Build(string macro, string? cancelSequence = null)
        {
            var body = Expand(macro);
            if (body.Length > 0 && !EndsWithTerminator(body)) body += "\n";
            return Expand(cancelSequence) + body;
        }

        /// <summary>Transparent form: "'" + command (AutoLISP expressions are sent as is).</summary>
        public static string BuildTransparent(string macro)
        {
            var body = Build(macro).TrimStart();
            if (body.StartsWith("'", StringComparison.Ordinal) || body.StartsWith("(", StringComparison.Ordinal)) return body;
            return "'" + body;
        }

        /// <summary>Human readable form for logs: control characters shown as ^C, ^[, \n.</summary>
        public static string Visualize(string? text) =>
            (text ?? string.Empty).Replace("\x03", "^C").Replace("\x1B", "^[").Replace("\n", "\\n").Replace("\r", "\\r");

        private static bool EndsWithTerminator(string body)
        {
            var last = body[body.Length - 1];
            return last == '\n' || last == '\r' || last == ' ';
        }
    }
}
