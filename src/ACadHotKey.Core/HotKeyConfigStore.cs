using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace ACadHotKey.Core
{
    /// <summary>First existing configuration wins; failed reloads preserve the complete working configuration.</summary>
    public sealed class HotKeyConfigStore
    {
        public HotKeyConfig Current { get; private set; } = new HotKeyConfig();
        public string? Path { get; private set; }

        public bool TryReload(IEnumerable<string> searchPaths, out string message)
        {
            var paths = searchPaths.ToArray();
            var path = paths.FirstOrDefault(File.Exists);
            if (path == null)
            {
                message = "configuration file not found (" + string.Join(" or ", paths) + "); previous settings retained.";
                return false;
            }
            try
            {
                var config = HotKeyConfig.Load(path);
                Current = config;
                Path = path;
                message = config.Bindings.Count + " hotkey(s) loaded from " + path + ".";
                return true;
            }
            catch (Exception ex) when (ex is FormatException || ex is IOException || ex is XmlException || ex is UnauthorizedAccessException)
            {
                message = "configuration error in " + path + ": " + ex.Message + "; previous settings retained.";
                return false;
            }
        }
    }
}
