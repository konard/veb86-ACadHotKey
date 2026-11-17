using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml;

namespace ACadHotKey.Core
{
    /// <summary>What to do when a hotkey is pressed while an AutoCAD command is running.</summary>
    public enum CommandActivePolicy
    {
        /// <summary>Cancel the running command (like Esc, "^C^C") and start the hotkey macro.</summary>
        Interrupt,

        /// <summary>Do not handle the key: AutoCAD receives it as normal input (e.g. a digit of a distance).</summary>
        PassThrough,

        /// <summary>Run the macro transparently ("'ZOOM") without cancelling the running command.</summary>
        Transparent,
    }

    /// <summary>Kind of input the active prompt is waiting for (from Editor.PromptingForXxx events).</summary>
    public enum PromptKind
    {
        None,
        Point,
        Corner,
        Distance,
        Angle,
        Double,
        Integer,
        String,
        Keyword,
        Selection,
        Entity,
        NestedEntity,
    }

    /// <summary>One hotkey binding from the configuration file.</summary>
    public sealed class HotKeyBinding
    {
        public HotKeyBinding(KeyChord key, string macro)
        {
            Key = key;
            Macro = macro ?? throw new ArgumentNullException(nameof(macro));
        }

        public KeyChord Key { get; }

        /// <summary>Text sent to the command line, e.g. "_LINE", "(c:MyFunc)", "^C^C_.ZOOM _E".</summary>
        public string Macro { get; }

        /// <summary>Overrides <see cref="HotKeyConfig.DefaultWhenCommandActive"/> when set.</summary>
        public CommandActivePolicy? WhenCommandActive { get; set; }

        /// <summary>While the active prompt is of one of these kinds, the key is passed to AutoCAD.</summary>
        public ISet<PromptKind> PassThroughOnPrompts { get; } = new HashSet<PromptKind>();

        public string Description { get; set; } = string.Empty;

        public override string ToString() => $"{Key} -> {Macro}";
    }

    /// <summary>Bindings indexed by virtual key and modifiers; mutations keep the index in sync.</summary>
    public sealed class HotKeyBindings : KeyedCollection<KeyChord, HotKeyBinding>
    {
        public HotKeyBindings() : base(null, 0) { }
        protected override KeyChord GetKeyForItem(HotKeyBinding item) => item.Key;
        public HotKeyBinding? Find(KeyChord key) => Contains(key) ? this[key] : null;
    }

    /// <summary>Plugin configuration (see config/ACadHotKey.config.xml).</summary>
    public sealed class HotKeyConfig
    {
        /// <summary>Commands during which keys are always passed to AutoCAD (in-place text editors).</summary>
        public static readonly string[] DefaultExcludedCommands =
        {
            "MTEXT", "TEXT", "DTEXT", "MTEDIT", "TEXTEDIT", "DDEDIT", "ATTIPEDIT", "TABLEDIT",
            "TABLE", "MLEADER", "QLEADER", "LEADER", "DIMEDIT",
        };

        /// <summary>Window class name fragments that denote text input controls (never intercepted).</summary>
        public static readonly string[] DefaultDeniedWindowClasses = { "Edit", "RichEdit", "ComboBox" };

        public bool Enabled { get; set; } = true;

        /// <summary>Prefix sent before the macro to interrupt a running command. "^C" means Ctrl+C (Esc).</summary>
        public string CancelSequence { get; set; } = "^C^C";

        public CommandActivePolicy DefaultWhenCommandActive { get; set; } = CommandActivePolicy.Interrupt;

        /// <summary>
        /// When the user has already typed something at the current prompt (e.g. "@100"),
        /// hotkeys are passed to AutoCAD until Enter/Space/Esc or until the command changes.
        /// </summary>
        public bool PassThroughWhileTyping { get; set; } = true;

        /// <summary>Do not react to hotkeys while a script (CMDACTIVE bit 4) is running.</summary>
        public bool IgnoreDuringScript { get; set; } = true;

        /// <summary>Echo the sent macro on the command line.</summary>
        public bool EchoMacro { get; set; } = true;

        public ISet<string> ExcludedCommands { get; } =
            new HashSet<string>(DefaultExcludedCommands, StringComparer.OrdinalIgnoreCase);

        public IList<string> DeniedWindowClasses { get; } = new List<string>(DefaultDeniedWindowClasses);

        public HotKeyBindings Bindings { get; } = new HotKeyBindings();

        public HotKeyBinding? Find(KeyChord key) => Bindings.Find(key);

        public static HotKeyConfig Load(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                return Load(XDocument.Load(stream, LoadOptions.SetLineInfo));
            }
        }

        public static HotKeyConfig Parse(string xml) => Load(XDocument.Parse(xml, LoadOptions.SetLineInfo));

        /// <summary>Reads the XML configuration. Throws <see cref="FormatException"/> with a readable message.</summary>
        public static HotKeyConfig Load(XDocument document)
        {
            var root = document.Root ?? throw new FormatException("Configuration is empty.");
            if (root.Name != "ACadHotKey")
                throw new FormatException($"Root element must be <ACadHotKey>, found <{root.Name.LocalName}>.");

            ValidateAttributes(root, "enabled", "whenCommandActive", "cancelSequence", "passThroughWhileTyping",
                "ignoreDuringScript", "echoMacro");
            foreach (var child in root.Elements())
            {
                if (child.Name != "HotKey" && child.Name != "ExcludedCommands" && child.Name != "DeniedWindowClasses")
                    throw Error(child, $"Unknown element <{child.Name}>.");
                if (child.HasElements) throw Error(child, $"<{child.Name}> must not contain child elements.");
                ValidateAttributes(child, child.Name == "HotKey"
                    ? new[] { "key", "macro", "whenCommandActive", "passThroughOnPrompts", "description" }
                    : new[] { "keepDefaults" });
            }
            foreach (var list in new[] { "ExcludedCommands", "DeniedWindowClasses" })
                if (root.Elements(list).Skip(1).Any()) throw Error(root, $"<{list}> is defined more than once.");

            var config = new HotKeyConfig();
            config.Enabled = ReadBool(root, "enabled", config.Enabled);
            config.CancelSequence = (string?)root.Attribute("cancelSequence") ?? config.CancelSequence;
            config.PassThroughWhileTyping = ReadBool(root, "passThroughWhileTyping", config.PassThroughWhileTyping);
            config.IgnoreDuringScript = ReadBool(root, "ignoreDuringScript", config.IgnoreDuringScript);
            config.EchoMacro = ReadBool(root, "echoMacro", config.EchoMacro);
            var policy = (string?)root.Attribute("whenCommandActive");
            if (policy != null) config.DefaultWhenCommandActive = ParsePolicy(root, policy);

            var excluded = root.Element("ExcludedCommands");
            if (excluded != null)
            {
                if (!ReadBool(excluded, "keepDefaults", true)) config.ExcludedCommands.Clear();
                foreach (var name in SplitList(excluded.Value)) config.ExcludedCommands.Add(name);
            }

            var denied = root.Element("DeniedWindowClasses");
            if (denied != null)
            {
                if (!ReadBool(denied, "keepDefaults", true)) config.DeniedWindowClasses.Clear();
                foreach (var name in SplitList(denied.Value)) config.DeniedWindowClasses.Add(name);
            }

            foreach (var element in root.Elements("HotKey"))
            {
                var keyText = (string?)element.Attribute("key")
                    ?? throw new FormatException("<HotKey> requires the 'key' attribute.");
                if (!KeyChord.TryParse(keyText, out var chord, out var error)) throw new FormatException(error);

                var macro = (string?)element.Attribute("macro");
                if (string.IsNullOrWhiteSpace(macro))
                    throw new FormatException($"<HotKey key=\"{keyText}\"> requires a non-empty 'macro' attribute.");

                if (config.Find(chord) != null)
                    throw new FormatException($"Hotkey '{chord}' is defined more than once.");

                var binding = new HotKeyBinding(chord, macro!)
                {
                    Description = (string?)element.Attribute("description") ?? string.Empty,
                };
                var bindingPolicy = (string?)element.Attribute("whenCommandActive");
                if (bindingPolicy != null) binding.WhenCommandActive = ParsePolicy(element, bindingPolicy);

                foreach (var kind in SplitList((string?)element.Attribute("passThroughOnPrompts") ?? string.Empty))
                {
                    if (!Enum.GetNames(typeof(PromptKind)).Any(n => n.Equals(kind, StringComparison.OrdinalIgnoreCase))
                        || !Enum.TryParse<PromptKind>(kind, true, out var parsed) || parsed == PromptKind.None)
                        throw Error(element, $"Unknown prompt kind '{kind}' in hotkey '{keyText}'.");
                    binding.PassThroughOnPrompts.Add(parsed);
                }

                config.Bindings.Add(binding);
            }

            return config;
        }

        private static CommandActivePolicy ParsePolicy(XElement element, string text)
        {
            if (Enum.GetNames(typeof(CommandActivePolicy)).Any(n => n.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase))
                && Enum.TryParse<CommandActivePolicy>(text.Trim(), true, out var policy)) return policy;
            throw Error(element,
                $"Unknown whenCommandActive value '{text}'. Expected Interrupt, PassThrough or Transparent.");
        }

        private static void ValidateAttributes(XElement element, params string[] allowed)
        {
            foreach (var attribute in element.Attributes())
                if (!allowed.Any(name => attribute.Name == name))
                    throw Error(element, $"Unknown attribute '{attribute.Name}' on <{element.Name}>.");
        }

        private static FormatException Error(XElement element, string message)
        {
            var key = (string?)element.Attribute("key");
            var info = (IXmlLineInfo)element;
            return new FormatException(message + (key == null ? "" : $" Hotkey '{key}'.")
                + (info.HasLineInfo() ? $" Line {info.LineNumber}." : ""));
        }

        private static bool ReadBool(XElement element, string name, bool fallback)
        {
            var value = (string?)element.Attribute(name);
            if (value == null) return fallback;
            if (bool.TryParse(value.Trim(), out var result)) return result;
            if (value.Trim() == "1") return true;
            if (value.Trim() == "0") return false;
            throw new FormatException(string.Format(CultureInfo.InvariantCulture,
                "Attribute '{0}' of <{1}> must be true or false, found '{2}'.", name, element.Name.LocalName, value));
        }

        private static IEnumerable<string> SplitList(string text) =>
            text.Split(new[] { ' ', ',', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    }
}
