using System;
using System.Globalization;
using System.IO;

namespace ACadHotKey.Plugin
{
    /// <summary>
    /// Diagnostic log, switched off by default. Enable with the ACHK_DIAG command
    /// or the environment variable ACADHOTKEY_TRACE=1. File: %TEMP%\ACadHotKey.log.
    /// </summary>
    internal static class TraceLog
    {
        internal static readonly string FilePath = Path.Combine(Path.GetTempPath(), "ACadHotKey.log");

        internal static bool Enabled { get; set; } =
            Environment.GetEnvironmentVariable("ACADHOTKEY_TRACE") == "1";

        internal static void Write(string message)
        {
            if (!Enabled) return;
            try
            {
                File.AppendAllText(FilePath,
                    DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine);
            }
            catch (IOException)
            {
                // Diagnostics must never break AutoCAD input processing.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
