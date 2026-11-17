using System;

namespace ACadHotKey.Core
{
    /// <summary>Queues macros before consuming input, with rollback if the host rejects the command.</summary>
    public static class KeyMessageProcessor
    {
        public static KeyDecision Process(HotKeyEngine engine, KeyboardMessage message, EditorState state,
            Action<string> queueMacro, Action<Exception> reportError)
        {
            try
            {
                var decision = engine.Process(message, state);
                if (decision.Action == KeyAction.Execute && decision.Text != null) queueMacro(decision.Text);
                return decision;
            }
            catch (Exception error)
            {
                engine.ReleaseKey(message.VirtualKey);
                try { reportError(error); } catch (Exception) { /* Logging must not break keyboard input. */ }
                return KeyDecision.Pass("macro dispatch failed");
            }
        }
    }
}
