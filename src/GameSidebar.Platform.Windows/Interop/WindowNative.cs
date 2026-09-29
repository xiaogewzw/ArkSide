using System.Diagnostics;
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
        string? exe = null;
        DateTimeOffset? started = null;
        WindowOperationError? error = null;
        try
        {
            using var process = Process.GetProcessById(checked((int)pid));
            try { started = new DateTimeOffset(process.StartTime); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            { error = new(WindowErrorCode.PermissionDenied, $"无法读取进程创建时间：{e.Message}"); }
            try { exe = Path.GetFileName(process.MainModule?.FileName); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            { error ??= new(WindowErrorCode.PermissionDenied, $"无法读取进程路径：{e.Message}"); }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { error = new(WindowErrorCode.WindowDestroyed, $"进程不可用：{e.Message}"); }
        return new(id, checked((int)pid), exe, title.ToString(), className.ToString(), started,
            NativeMethods.IsWindowVisible(hwnd), NativeMethods.IsIconic(hwnd), (style & 0x80) != 0,
            cloaked, NativeMethods.GetWindow(hwnd, 4) != 0, pid == Environment.ProcessId, error);
    }
}
