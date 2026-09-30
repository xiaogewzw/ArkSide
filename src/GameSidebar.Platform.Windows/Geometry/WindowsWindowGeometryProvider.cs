using System.Runtime.Versioning;
using GameSidebar.Application.Abstractions;
using GameSidebar.Core.Geometry;
using GameSidebar.Core.Sessions;
using GameSidebar.Platform.Windows.Interop;

namespace GameSidebar.Platform.Windows.Geometry;

[SupportedOSPlatform("windows")]
public sealed class WindowsWindowGeometryProvider : IWindowGeometryProvider
{
    public Task<WindowReadResult> ReadAsync(WindowIdentity identity, CancellationToken cancellationToken) =>
        Task.Run(() => Read(identity, cancellationToken), cancellationToken);

    private static WindowReadResult Read(WindowIdentity requested, CancellationToken cancellationToken)
    {
        if (!WindowNative.TryHandle(requested.Id, out var hwnd) || !NativeMethods.IsWindow(hwnd))
            return Lost(requested, IdentityVerification.Destroyed, "窗口句柄无效");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = Verify(hwnd, requested);
            if (before.Verification is IdentityVerification.Destroyed or IdentityVerification.Mismatch)
                return new(before, null, false, false, new(WindowErrorCode.WindowDestroyed, "窗口归属或进程创建时间变化"));
            var minimized = NativeMethods.IsIconic(hwnd);
            var foreground = NativeMethods.GetForegroundWindow() == hwnd;
            var caller = WindowNative.Awareness(NativeMethods.GetThreadDpiAwarenessContext());
            var target = WindowNative.Awareness(NativeMethods.GetWindowDpiAwarenessContext(hwnd));
            if (caller is not (DpiAwarenessKind.PerMonitorAware or DpiAwarenessKind.PerMonitorAwareV2))
                return new(before, null, foreground, minimized,
                    new(WindowErrorCode.ApiFailure, $"调用线程 DPI awareness={caller}，无法保证物理像素坐标"));
            if (minimized) return new(before, null, foreground, true,
                new(WindowErrorCode.ApiFailure, "窗口已最小化，Client 几何暂不可用"));
            if (!NativeMethods.GetWindowRect(hwnd, out var window))
                return Failure(before, foreground, false, "GetWindowRect");
            if (!NativeMethods.GetClientRect(hwnd, out var client))
                return Failure(before, foreground, false, "GetClientRect");
            var origin = new NativeMethods.Point();
            if (!NativeMethods.ClientToScreen(hwnd, ref origin))
                return Failure(before, foreground, false, "ClientToScreen");
            var monitorHandle = NativeMethods.MonitorFromWindow(hwnd, 2);
            if (monitorHandle == 0) return Failure(before, foreground, false, "MonitorFromWindow");
            var monitor = new NativeMethods.MonitorInfo { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
            if (!NativeMethods.GetMonitorInfo(monitorHandle, ref monitor))
                return Failure(before, foreground, false, "GetMonitorInfo");
            var windowRect = WindowNative.Rect(window);
            var source = VisibleFrameSource.Dwm;
            var frame = windowRect;
            if (NativeMethods.DwmGetWindowAttribute(hwnd, 9, out NativeMethods.Rect visible,
                    (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.Rect>()) == 0)
                frame = WindowNative.Rect(visible);
            else source = VisibleFrameSource.WindowBoundsFallback;
            var size = new ClientPixelSize(client.Right - client.Left, client.Bottom - client.Top);
            var clientOrigin = new ScreenPixelPoint(origin.X, origin.Y);
            var clientBounds = new ScreenPixelRect(origin.X, origin.Y, origin.X + size.Width, origin.Y + size.Height);
            var dpi = NativeMethods.GetDpiForWindow(hwnd);
            var valid = dpi > 0 && size.IsValid && windowRect.IsValid && frame.IsValid && clientBounds.IsValid &&
                windowRect.Width > 0 && windowRect.Height > 0;
            var after = Verify(hwnd, requested);
            if (after.Verification is IdentityVerification.Destroyed or IdentityVerification.Mismatch)
                return new(after, null, foreground, false, new(WindowErrorCode.WindowDestroyed, "采样期间窗口身份变化"));
            if (!valid && attempt == 0) continue;
            var geometry = new WindowGeometrySnapshot(windowRect, frame, source, size, clientOrigin,
                clientBounds, WindowNative.Rect(monitor.Monitor), WindowNative.Rect(monitor.Work),
                $"0x{monitorHandle.ToInt64():X}", dpi, target, caller,
                valid ? GeometryValidity.Valid : GeometryValidity.Inconsistent, DateTimeOffset.UtcNow,
                new WindowPlacement(DesktopCoordinateSpace.WindowsPhysicalPixels,
                    new(frame.Left, frame.Top, frame.Width, frame.Height),
                    new(monitor.Work.Left, monitor.Work.Top,
                        monitor.Work.Right - monitor.Work.Left, monitor.Work.Bottom - monitor.Work.Top),
                    $"0x{monitorHandle.ToInt64():X}", null, true));
            return new(after, geometry, foreground, false,
                !valid ? new(WindowErrorCode.ApiFailure, "采样到矛盾或无效的窗口几何") :
                after.Verification == IdentityVerification.Insufficient ?
                    new(WindowErrorCode.IdentityInsufficient, "进程创建时间暂不可读") : null);
        }
        return Failure(requested, false, false, "Geometry retry");
    }

    private static WindowIdentity Verify(nint hwnd, WindowIdentity requested)
    {
        if (!NativeMethods.IsWindow(hwnd)) return requested with { Verification = IdentityVerification.Destroyed };
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid != requested.ProcessId) return requested with { Verification = IdentityVerification.Mismatch };
        var process = WindowsProcessInfo.Read(pid);
        if (process.StartedAt is null) return requested with { Verification = IdentityVerification.Insufficient };
        var started = process.StartedAt;
        if (requested.ProcessStartedAt is not null && started != requested.ProcessStartedAt)
            return requested with { Verification = IdentityVerification.Mismatch };
        return requested with { ProcessStartedAt = started,
            Verification = IdentityVerification.Verified };
    }

    private static WindowReadResult Failure(WindowIdentity identity, bool foreground, bool minimized, string api) =>
        new(identity, null, foreground, minimized,
            new(WindowErrorCode.ApiFailure, $"{api} 调用失败", WindowNative.LastError()));
    private static WindowReadResult Lost(WindowIdentity identity, IdentityVerification status, string message) =>
        new(identity with { Verification = status }, null, false, false,
            new(WindowErrorCode.WindowDestroyed, message));
}
