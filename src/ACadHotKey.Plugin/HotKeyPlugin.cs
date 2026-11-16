using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ACadHotKey.Core;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(ACadHotKey.Plugin.HotKeyPlugin))]
[assembly: CommandClass(typeof(ACadHotKey.Plugin.HotKeyCommands))]

namespace ACadHotKey.Plugin
{
    /// <summary>
    /// Entry point. Subscribes to Application.PreTranslateMessage (the managed wrapper of
    /// ObjectARX acedRegisterFilterWinMsg): every Windows message of the AutoCAD UI thread
    /// passes through <see cref="OnPreTranslateMessage"/> before AutoCAD translates/dispatches it,
    /// and setting Handled = true removes it, so the character never reaches the command line.
    /// </summary>
    public sealed class HotKeyPlugin : IExtensionApplication
    {
        internal const string ConfigFileName = "ACadHotKey.config.xml";

        internal static HotKeyPlugin? Instance { get; private set; }

        internal HotKeyEngine Engine { get; private set; } = new HotKeyEngine(new HotKeyConfig());

        internal string? ConfigPath { get; private set; }

        private readonly PromptTracker _prompts = new PromptTracker();

        public void Initialize()
        {
            Instance = this;
            ReloadConfig(out var message);

            var docs = AcApp.DocumentManager;
            foreach (Document doc in docs) AttachDocument(doc);
            docs.DocumentCreated += OnDocumentCreated;
            docs.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
            docs.DocumentActivated += OnDocumentActivated;

            AcApp.PreTranslateMessage += OnPreTranslateMessage;

            docs.MdiActiveDocument?.Editor.WriteMessage("\nACadHotKey: " + message +
                " Commands: ACHK_LIST, ACHK_ON, ACHK_OFF, ACHK_RELOAD, ACHK_DIAG.\n");
        }

        public void Terminate()
        {
            AcApp.PreTranslateMessage -= OnPreTranslateMessage;
            Instance = null;
        }

        /// <summary>
        /// Config search order: %APPDATA%\ACadHotKey\ACadHotKey.config.xml (per user),
        /// then the file next to ACadHotKey.dll.
        /// </summary>
        internal bool ReloadConfig(out string message)
        {
            var userPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ACadHotKey", ConfigFileName);
            var dllPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", ConfigFileName);
            var path = new[] { userPath, dllPath }.FirstOrDefault(File.Exists);
            if (path == null)
            {
                message = "configuration file not found (" + userPath + " or " + dllPath + "), hotkeys are not defined.";
                return false;
            }

            try
            {
                var config = HotKeyConfig.Load(path);
                Engine.Config = config;
                ConfigPath = path;
                message = config.Bindings.Count + " hotkey(s) loaded from " + path + ".";
                TraceLog.Write(message);
                return true;
            }
            catch (System.Exception ex) when (ex is FormatException || ex is IOException || ex is System.Xml.XmlException || ex is UnauthorizedAccessException)
            {
                // Keep the previous (working) configuration.
                message = "configuration error in " + path + ": " + ex.Message;
                TraceLog.Write(message);
                return false;
            }
        }

        private void AttachDocument(Document doc)
        {
            _prompts.Attach(doc);
            doc.CommandWillStart += OnCommandStateChanged;
            doc.CommandEnded += OnCommandStateChanged;
            doc.CommandCancelled += OnCommandStateChanged;
            doc.CommandFailed += OnCommandStateChanged;
        }

        private void OnDocumentCreated(object sender, DocumentCollectionEventArgs e)
        {
            if (e.Document != null) AttachDocument(e.Document);
        }

        private void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e)
        {
            if (e.Document != null) _prompts.Forget(e.Document);
        }

        private void OnDocumentActivated(object sender, DocumentCollectionEventArgs e) => Engine.ResetTyping();

        private void OnCommandStateChanged(object sender, CommandEventArgs e)
        {
            if (sender is Document doc) _prompts.Reset(doc);
            Engine.ResetTyping();
            TraceLog.Write("command event " + e.GlobalCommandName);
        }

        private void OnPreTranslateMessage(object sender, PreTranslateMessageEventArgs e)
        {
            var msg = e.Message;
            // Fast path: this handler sees every message of the UI thread (mouse moves, paints...).
            if (!WindowMessages.IsKeyDown(msg.message) && !WindowMessages.IsKeyUp(msg.message)) return;

            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                var key = new KeyboardMessage(msg.message, unchecked((int)msg.wParam.ToInt64()) & 0xFF,
                    NativeMethods.GetModifiers(), WindowMessages.IsRepeat(msg.lParam.ToInt64()));

                var docWindow = doc.Window?.Handle ?? IntPtr.Zero;
                var isDocChild = docWindow != IntPtr.Zero && (msg.hwnd == docWindow || NativeMethods.IsChild(docWindow, msg.hwnd));
                var className = NativeMethods.GetClassName(msg.hwnd);
                var state = new EditorState
                {
                    DrawingHasFocus = FocusPolicy.IsDrawingArea(isDocChild, className, Engine.Config.DeniedWindowClasses),
                    CommandNames = AcApp.GetSystemVariable("CMDNAMES") as string ?? string.Empty,
                    CmdActive = Convert.ToInt32(AcApp.GetSystemVariable("CMDACTIVE")),
                    Prompt = _prompts.Get(doc),
                };

                var decision = Engine.Process(key, state);

                if (TraceLog.Enabled && WindowMessages.IsKeyDown(msg.message))
                {
                    TraceLog.Write(string.Format(
                        "key {0} hwnd {1} class '{2}' docWindow {3} isDocChild {4} chain [{5}] CMDNAMES '{6}' CMDACTIVE {7} prompt {8} typing {9} => {10}",
                        new KeyChord(key.VirtualKey, key.Modifiers), NativeMethods.Hex(msg.hwnd), className,
                        NativeMethods.Hex(docWindow), isDocChild, NativeMethods.DescribeParentChain(msg.hwnd),
                        state.CommandNames, state.CmdActive, state.Prompt, Engine.IsTyping, decision));
                }

                if (!decision.Handled) return;
                e.Handled = true;

                if (decision.Action == KeyAction.Execute && decision.Text != null)
                {
                    _prompts.Reset(doc);
                    // Queues the text into the command input stream of the document: "^C^C" cancels
                    // the running command, then the macro starts. Safe to call from application context.
                    doc.SendStringToExecute(decision.Text, true, false, Engine.Config.EchoMacro);
                }
            }
            catch (System.Exception ex)
            {
                // Never let an exception escape into AutoCAD's message loop.
                TraceLog.Write("error: " + ex);
            }
        }
    }
}
