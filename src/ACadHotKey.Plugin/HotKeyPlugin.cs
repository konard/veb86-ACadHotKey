using System;
using System.Collections.Generic;
using System.IO;
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

        internal string? ConfigPath => _configStore.Path;

        private readonly HotKeyConfigStore _configStore = new HotKeyConfigStore();
        private readonly HashSet<Document> _documents = new HashSet<Document>();
        private readonly PromptTracker _prompts;
        private bool _modal;

        public HotKeyPlugin()
        {
            _prompts = new PromptTracker(doc =>
            {
                if (doc == AcApp.DocumentManager.MdiActiveDocument) Engine.ResetTyping();
            });
        }

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
            AcApp.EnterModal += OnEnterModal;
            AcApp.LeaveModal += OnLeaveModal;

            docs.MdiActiveDocument?.Editor.WriteMessage("\nACadHotKey: " + message +
                " Commands: ACHK_LIST, ACHK_ON, ACHK_OFF, ACHK_RELOAD, ACHK_DIAG.\n");
        }

        public void Terminate()
        {
            AcApp.PreTranslateMessage -= OnPreTranslateMessage;
            AcApp.EnterModal -= OnEnterModal;
            AcApp.LeaveModal -= OnLeaveModal;
            var docs = AcApp.DocumentManager;
            docs.DocumentCreated -= OnDocumentCreated;
            docs.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
            docs.DocumentActivated -= OnDocumentActivated;
            foreach (var doc in new List<Document>(_documents)) DetachDocument(doc);
            Engine.ResetKeys();
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
            var loaded = _configStore.TryReload(new[] { userPath, dllPath }, out message);
            if (loaded) Engine.Config = _configStore.Current;
            TraceLog.Write(message);
            return loaded;
        }

        private void AttachDocument(Document doc)
        {
            if (!_documents.Add(doc)) return;
            _prompts.Attach(doc);
        }

        private void DetachDocument(Document doc)
        {
            if (!_documents.Remove(doc)) return;
            _prompts.Forget(doc);
        }

        private void OnDocumentCreated(object sender, DocumentCollectionEventArgs e)
        {
            if (e.Document != null) AttachDocument(e.Document);
        }

        private void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e)
        {
            if (e.Document != null) DetachDocument(e.Document);
        }

        private void OnDocumentActivated(object sender, DocumentCollectionEventArgs e) => Engine.ResetTyping();

        private void OnEnterModal(object sender, EventArgs e) => _modal = true;
        private void OnLeaveModal(object sender, EventArgs e) => _modal = false;

        private void OnPreTranslateMessage(object sender, PreTranslateMessageEventArgs e)
        {
            var msg = e.Message;
            // Fast path: this handler sees every message of the UI thread (mouse moves, paints...).
            if (!WindowMessages.IsKeyDown(msg.message) && !WindowMessages.IsKeyUp(msg.message)) return;

            try
            {
                if (e.Handled) return;
                if (!NativeMethods.IsApplicationForeground())
                {
                    Engine.ResetKeys();
                    return;
                }
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                var key = new KeyboardMessage(msg.message, unchecked((int)msg.wParam.ToInt64()) & 0xFF,
                    NativeMethods.GetModifiers(), WindowMessages.IsRepeat(msg.lParam.ToInt64()));

                // Release a consumed key even if the active document was closed while it was held.
                if (WindowMessages.IsKeyUp(msg.message))
                {
                    e.Handled = Engine.Process(key, new EditorState()).Handled;
                    if (TraceLog.Enabled) TraceLog.Write(string.Format(
                        "key-up {0} hwnd {1} class '{2}' chain [{3}] CMDNAMES '{4}' CMDACTIVE {5} prompt {6} handled {7}",
                        key.VirtualKey, NativeMethods.Hex(msg.hwnd), NativeMethods.GetClassName(msg.hwnd),
                        NativeMethods.DescribeParentChain(msg.hwnd), AcApp.GetSystemVariable("CMDNAMES"),
                        AcApp.GetSystemVariable("CMDACTIVE"), doc == null ? PromptKind.None : _prompts.Get(doc), e.Handled));
                    return;
                }
                if (doc == null) return;

                var docWindow = doc.Window?.Handle ?? IntPtr.Zero;
                var isDocChild = docWindow != IntPtr.Zero && (msg.hwnd == docWindow || NativeMethods.IsChild(docWindow, msg.hwnd));
                var className = NativeMethods.GetClassName(msg.hwnd);
                var state = new EditorState
                {
                    DrawingHasFocus = !_modal && FocusPolicy.IsDrawingArea(msg.hwnd, docWindow, NativeMethods.GetFocus(),
                        NativeMethods.GetParent, NativeMethods.GetClassName, Engine.Config.DeniedWindowClasses),
                    CommandNames = AcApp.GetSystemVariable("CMDNAMES") as string ?? string.Empty,
                    CmdActive = Convert.ToInt32(AcApp.GetSystemVariable("CMDACTIVE")),
                    Prompt = _prompts.Get(doc),
                };

                var decision = KeyMessageProcessor.Process(Engine, key, state,
                    text => doc.SendStringToExecute(text, true, false, Engine.Config.EchoMacro),
                    error => TraceLog.Write("dispatch error: " + error));
                e.Handled = decision.Handled;

                if (TraceLog.Enabled && WindowMessages.IsKeyDown(msg.message))
                {
                    TraceLog.Write(string.Format(
                        "key {0} hwnd {1} class '{2}' docWindow {3} isDocChild {4} chain [{5}] CMDNAMES '{6}' CMDACTIVE {7} prompt {8} typing {9} => {10}",
                        new KeyChord(key.VirtualKey, key.Modifiers), NativeMethods.Hex(msg.hwnd), className,
                        NativeMethods.Hex(docWindow), isDocChild, NativeMethods.DescribeParentChain(msg.hwnd),
                        state.CommandNames, state.CmdActive, state.Prompt, Engine.IsTyping, decision));
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
