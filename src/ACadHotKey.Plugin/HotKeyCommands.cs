using ACadHotKey.Core;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace ACadHotKey.Plugin
{
    /// <summary>Service commands of the plugin.</summary>
    public sealed class HotKeyCommands
    {
        private static Editor? Ed => AcApp.DocumentManager.MdiActiveDocument?.Editor;

        [CommandMethod("ACHK_ON", CommandFlags.Transparent)]
        public void On() => SetEnabled(true);

        [CommandMethod("ACHK_OFF", CommandFlags.Transparent)]
        public void Off() => SetEnabled(false);

        [CommandMethod("ACHK_TOGGLE", CommandFlags.Transparent)]
        public void Toggle()
        {
            var plugin = HotKeyPlugin.Instance;
            if (plugin != null) SetEnabled(!plugin.Engine.Enabled);
        }

        [CommandMethod("ACHK_RELOAD")]
        public void Reload()
        {
            var plugin = HotKeyPlugin.Instance;
            if (plugin == null) return;
            plugin.ReloadConfig(out var message);
            Ed?.WriteMessage("\nACadHotKey: " + message);
        }

        [CommandMethod("ACHK_LIST")]
        public void List()
        {
            var plugin = HotKeyPlugin.Instance;
            var ed = Ed;
            if (plugin == null || ed == null) return;
            var config = plugin.Engine.Config;
            ed.WriteMessage("\nACadHotKey: {0}, config: {1}", plugin.Engine.Enabled ? "ON" : "OFF",
                plugin.ConfigPath ?? "<none>");
            foreach (var b in config.Bindings)
            {
                ed.WriteMessage("\n  {0,-14} {1,-30} {2,-12} {3}", b.Key, b.Macro,
                    b.WhenCommandActive ?? config.DefaultWhenCommandActive,
                    b.Description + (b.PassThroughOnPrompts.Count == 0 ? "" : " [input: " + string.Join(", ", b.PassThroughOnPrompts) + "]"));
            }
        }

        [CommandMethod("ACHK_DIAG", CommandFlags.Transparent)]
        public void Diag()
        {
            TraceLog.Enabled = !TraceLog.Enabled;
            Ed?.WriteMessage("\nACadHotKey: diagnostic log {0}: {1}", TraceLog.Enabled ? "ON" : "OFF", TraceLog.FilePath);
        }

        private static void SetEnabled(bool enabled)
        {
            var plugin = HotKeyPlugin.Instance;
            if (plugin == null) return;
            plugin.Engine.Enabled = enabled;
            Ed?.WriteMessage("\nACadHotKey: hotkeys {0}.", enabled ? "ON" : "OFF");
        }
    }
}
