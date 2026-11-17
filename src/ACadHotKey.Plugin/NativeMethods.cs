using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using ACadHotKey.Core;

namespace ACadHotKey.Plugin
{
    internal static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        internal static bool IsApplicationForeground()
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var processId);
            return processId == GetCurrentProcessId();
        }

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        internal static string GetClassName(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            return GetClassName(hWnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
        }

        internal static KeyModifiers GetModifiers()
        {
            var result = KeyModifiers.None;
            if (GetKeyState(VirtualKeys.Shift) < 0) result |= KeyModifiers.Shift;
            if (GetKeyState(VirtualKeys.Control) < 0) result |= KeyModifiers.Ctrl;
            if (GetKeyState(VirtualKeys.Menu) < 0) result |= KeyModifiers.Alt;
            return result;
        }

        /// <summary>"0x1234:ClassA > 0x5678:ClassB > ..." from the window up to the top-level window (diagnostics).</summary>
        internal static string DescribeParentChain(IntPtr hWnd)
        {
            var parts = new List<string>();
            for (var h = hWnd; h != IntPtr.Zero && parts.Count < 12; h = GetParent(h))
                parts.Add(Hex(h) + ":" + GetClassName(h));
            return string.Join(" > ", parts);
        }

        internal static string Hex(IntPtr h) => "0x" + h.ToInt64().ToString("X", CultureInfo.InvariantCulture);
    }
}
