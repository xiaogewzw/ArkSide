using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using GameSidebar.Core.Geometry;
using GameSidebar.Core.Sessions;

namespace GameSidebar.Platform.Windows.Interop;

internal static class WindowNative
{
    internal static WindowId Id(nint hwnd) => new($"0x{hwnd.ToInt64():X}");
    internal static bool TryHandle(WindowId id, out nint hwnd)
    {
        hwnd = 0;
        return id.Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(id.Value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number) &&
            (hwnd = new nint(number)) != 0;
    }
    internal static int LastError() => Marshal.GetLastWin32Error();
    internal static DpiAwarenessKind Awareness(nint context)
    {
        if (context == 0) return DpiAwarenessKind.Unknown;
        if (NativeMethods.AreDpiAwarenessContextsEqual(context, new nint(-4))) return DpiAwarenessKind.PerMonitorAwareV2;
        if (NativeMethods.AreDpiAwarenessContextsEqual(context, new nint(-3))) return DpiAwarenessKind.PerMonitorAware;
        if (NativeMethods.AreDpiAwarenessContextsEqual(context, new nint(-2))) return DpiAwarenessKind.SystemAware;
        if (NativeMethods.AreDpiAwarenessContextsEqual(context, new nint(-1))) return DpiAwarenessKind.Unaware;
        return DpiAwarenessKind.Unknown;
    }
    internal static ScreenPixelRect Rect(NativeMethods.Rect value) => new(value.Left, value.Top, value.Right, value.Bottom);
    internal static WindowCandidate Candidate(nint hwnd)
    {
        var id = Id(hwnd);
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        var title = new StringBuilder(1024);
        var className = new StringBuilder(256);
        NativeMethods.GetWindowText(hwnd, title, title.Capacity);
        NativeMethods.GetClassName(hwnd, className, className.Capacity);
        var style = NativeMethods.GetWindowLongPtr(hwnd, -20).ToInt64();
        var cloaked = NativeMethods.DwmGetWindowAttribute(hwnd, 14, out int cloak, sizeof(int)) == 0 && cloak != 0;
        var process = WindowsProcessInfo.Read(pid);
        return new(id, checked((int)pid), process.Executable, title.ToString(), className.ToString(), process.StartedAt,
            NativeMethods.IsWindowVisible(hwnd), NativeMethods.IsIconic(hwnd), (style & 0x80) != 0,
            cloaked, NativeMethods.GetWindow(hwnd, 4) != 0, pid == Environment.ProcessId, process.Error,
            ExecutablePath: process.ExecutablePath);
    }
}
