using System;
using System.Collections.Generic;
using System.Globalization;

namespace ACadHotKey.Core
{
    /// <summary>Keyboard modifier state at the moment a key was pressed.</summary>
    [Flags]
    public enum KeyModifiers
    {
        None = 0,
        Shift = 1,
        Ctrl = 2,
        Alt = 4,
    }

    /// <summary>
    /// A hotkey: Windows virtual-key code plus exact modifier set.
    /// Virtual-key codes are layout independent, so "1" or "Q" work the same with
    /// the Russian and the English keyboard layout.
    /// </summary>
    public readonly struct KeyChord : IEquatable<KeyChord>
    {
        public KeyChord(int virtualKey, KeyModifiers modifiers = KeyModifiers.None)
        {
            VirtualKey = virtualKey;
            Modifiers = modifiers;
        }

        public int VirtualKey { get; }
        public KeyModifiers Modifiers { get; }

        public bool Equals(KeyChord other) => VirtualKey == other.VirtualKey && Modifiers == other.Modifiers;
        public override bool Equals(object? obj) => obj is KeyChord other && Equals(other);
        public override int GetHashCode() => (VirtualKey * 397) ^ (int)Modifiers;
        public static bool operator ==(KeyChord a, KeyChord b) => a.Equals(b);
        public static bool operator !=(KeyChord a, KeyChord b) => !a.Equals(b);

        public override string ToString()
        {
            var parts = new List<string>();
            if ((Modifiers & KeyModifiers.Ctrl) != 0) parts.Add("Ctrl");
            if ((Modifiers & KeyModifiers.Alt) != 0) parts.Add("Alt");
            if ((Modifiers & KeyModifiers.Shift) != 0) parts.Add("Shift");
            parts.Add(VirtualKeys.GetName(VirtualKey));
            return string.Join("+", parts);
        }

        /// <summary>
        /// Parses strings like "1", "Q", "Num1", "F5", "Ctrl+Shift+D", "Alt+`", "0x31".
        /// Tokens are separated by '+'; use "Plus" for the '+' key itself.
        /// </summary>
        public static KeyChord Parse(string text)
        {
            if (!TryParse(text, out var chord, out var error))
                throw new FormatException(error);
            return chord;
        }

        public static bool TryParse(string? text, out KeyChord chord, out string error)
        {
            chord = default;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "Key is empty.";
                return false;
            }

            var tokens = text!.Split('+');
            var modifiers = KeyModifiers.None;
            int? vk = null;
            foreach (var raw in tokens)
            {
                var token = raw.Trim();
                if (token.Length == 0)
                {
                    error = $"Invalid key '{text}': empty token (use 'Plus' for the '+' key).";
                    return false;
                }

                switch (token.ToUpperInvariant())
                {
                    case "CTRL":
                    case "CONTROL":
                        modifiers |= KeyModifiers.Ctrl;
                        continue;
                    case "SHIFT":
                        modifiers |= KeyModifiers.Shift;
                        continue;
                    case "ALT":
                        modifiers |= KeyModifiers.Alt;
                        continue;
                }

                if (vk != null)
                {
                    error = $"Invalid key '{text}': more than one non-modifier key.";
                    return false;
                }

                if (!VirtualKeys.TryGetCode(token, out var code))
                {
                    error = $"Invalid key '{text}': unknown key name '{token}'.";
                    return false;
                }

                vk = code;
            }

            if (vk == null)
            {
                error = $"Invalid key '{text}': only modifiers given.";
                return false;
            }

            chord = new KeyChord(vk.Value, modifiers);
            return true;
        }
    }

    /// <summary>Win32 virtual-key codes used by the plugin (see WinUser.h).</summary>
    public static class VirtualKeys
    {
        public const int Back = 0x08;
        public const int Tab = 0x09;
        public const int Return = 0x0D;
        public const int Shift = 0x10;
        public const int Control = 0x11;
        public const int Menu = 0x12; // Alt
        public const int Escape = 0x1B;
        public const int Space = 0x20;
        public const int LWin = 0x5B;
        public const int RWin = 0x5C;
        public const int NumPad0 = 0x60;
        public const int F1 = 0x70;
        public const int LShift = 0xA0;
        public const int RMenu = 0xA5;

        private static readonly Dictionary<string, int> NamedKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Space"] = Space,
            ["Tab"] = Tab,
            ["Enter"] = Return,
            ["Backspace"] = Back,
            ["Esc"] = Escape,
            ["Escape"] = Escape,
            ["Insert"] = 0x2D,
            ["Delete"] = 0x2E,
            ["Del"] = 0x2E,
            ["Home"] = 0x24,
            ["End"] = 0x23,
            ["PageUp"] = 0x21,
            ["PageDown"] = 0x22,
            ["Left"] = 0x25,
            ["Up"] = 0x26,
            ["Right"] = 0x27,
            ["Down"] = 0x28,
            ["Pause"] = 0x13,
            ["NumMultiply"] = 0x6A,
            ["NumAdd"] = 0x6B,
            ["NumSubtract"] = 0x6D,
            ["NumDecimal"] = 0x6E,
            ["NumDivide"] = 0x6F,
            ["Plus"] = 0xBB, // VK_OEM_PLUS ("=" / "+" key)
            ["`"] = 0xC0, // VK_OEM_3 (also "ё" on the Russian layout)
            ["Tilde"] = 0xC0,
            ["-"] = 0xBD,
            ["Minus"] = 0xBD,
            ["="] = 0xBB,
            ["["] = 0xDB,
            ["]"] = 0xDD,
            [";"] = 0xBA,
            ["'"] = 0xDE,
            [","] = 0xBC,
            ["."] = 0xBE,
            ["/"] = 0xBF,
            ["\\"] = 0xDC,
        };

        public static bool TryGetCode(string name, out int code)
        {
            code = 0;
            if (string.IsNullOrEmpty(name)) return false;

            if (NamedKeys.TryGetValue(name, out code)) return true;

            // Top-row digits and Latin letters: VK code equals the ASCII code of the upper-case char.
            if (name.Length == 1)
            {
                var c = char.ToUpperInvariant(name[0]);
                if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z'))
                {
                    code = c;
                    return true;
                }

                return false;
            }

            var upper = name.ToUpperInvariant();

            // Numeric keypad digits: Num0..Num9 / NumPad0..NumPad9.
            foreach (var prefix in new[] { "NUMPAD", "NUM" })
            {
                if (upper.StartsWith(prefix, StringComparison.Ordinal) && upper.Length == prefix.Length + 1)
                {
                    var d = upper[prefix.Length];
                    if (d >= '0' && d <= '9')
                    {
                        code = NumPad0 + (d - '0');
                        return true;
                    }
                }
            }

            // Function keys F1..F24.
            if (upper[0] == 'F' && int.TryParse(upper.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out var f)
                && f >= 1 && f <= 24)
            {
                code = F1 + f - 1;
                return true;
            }

            // Raw virtual-key code: 0x31.
            if (upper.StartsWith("0X", StringComparison.Ordinal)
                && int.TryParse(upper.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)
                && hex > 0 && hex < 0xFF)
            {
                code = hex;
                return true;
            }

            return false;
        }

        public static string GetName(int code)
        {
            if ((code >= '0' && code <= '9') || (code >= 'A' && code <= 'Z')) return ((char)code).ToString();
            if (code >= NumPad0 && code <= NumPad0 + 9) return "Num" + (code - NumPad0);
            if (code >= F1 && code < F1 + 24) return "F" + (code - F1 + 1);
            foreach (var pair in NamedKeys)
            {
                if (pair.Value == code) return pair.Key;
            }

            return "0x" + code.ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>Keys that never start "typing" by themselves (modifiers, Win keys).</summary>
        public static bool IsModifier(int code) =>
            code == Shift || code == Control || code == Menu || code == LWin || code == RWin
            || (code >= LShift && code <= RMenu);

        /// <summary>Keys that complete or abort an input at an AutoCAD prompt.</summary>
        public static bool IsInputTerminator(int code) => code == Return || code == Escape || code == Space;

        /// <summary>Keys that produce characters at the command line (letters, digits, punctuation).</summary>
        public static bool IsTypingKey(int code) =>
            (code >= '0' && code <= '9') || (code >= 'A' && code <= 'Z')
            || (code >= NumPad0 && code <= 0x6F)
            || (code >= 0xBA && code <= 0xC0) || (code >= 0xDB && code <= 0xDE)
            || code == Back;
    }
}
