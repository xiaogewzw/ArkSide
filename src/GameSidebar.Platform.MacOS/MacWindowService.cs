using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using GameSidebar.Application.Abstractions;
using GameSidebar.Core.Geometry;
using GameSidebar.Core.Sessions;

namespace GameSidebar.Platform.MacOS;

internal static class MacNative
{
    [DllImport("GameSidebarMac", EntryPoint = "GSWindowsJSON")]
    internal static extern nint WindowsJson();
    [DllImport("GameSidebarMac", EntryPoint = "GSBundleId", CharSet = CharSet.Ansi)]
    internal static extern nint BundleId(string path);
    [DllImport("GameSidebarMac", EntryPoint = "GSChooseApplication")]
    internal static extern nint ChooseApplication();
    [DllImport("GameSidebarMac", EntryPoint = "GSScreenPermission")]
    [return: MarshalAs(UnmanagedType.I1)] internal static extern bool ScreenPermission([MarshalAs(UnmanagedType.I1)] bool request);
    [DllImport("GameSidebarMac", EntryPoint = "GSMouseX")]
    internal static extern double MouseX();
    [DllImport("GameSidebarMac", EntryPoint = "GSMouseY")]
    internal static extern double MouseY();
    [DllImport("GameSidebarMac", EntryPoint = "GSCapturePNG")]
    internal static extern nint CapturePng(uint windowId, out int count, out nint error);
    [DllImport("GameSidebarMac", EntryPoint = "GSFree")]
    internal static extern void Free(nint pointer);
    internal static string? ReadString(nint pointer)
    {
        if (pointer == 0) return null;
        try { return Marshal.PtrToStringUTF8(pointer); }
        finally { Free(pointer); }
    }
}

public sealed record MacWindow(uint Id, int Pid, string Title, string Owner, string BundleId,
    string BundlePath, double X, double Y, double Width, double Height,
    bool Visible, bool Minimized, bool Self, string? DisplayId = null,
    double PixelsPerPoint = 0, double WorkX = 0, double WorkY = 0,
    double WorkWidth = 0, double WorkHeight = 0, bool Foreground = false);

public sealed class MacWindowService : IWindowCatalog, IWindowGeometryProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static IReadOnlyList<MacWindow> ReadWindows()
    {
        var json = MacNative.ReadString(MacNative.WindowsJson()) ?? "[]";
        return JsonSerializer.Deserialize<List<MacWindow>>(json, Json) ?? [];
    }
    public static string? ReadBundleId(string path) => MacNative.ReadString(MacNative.BundleId(path));
    public static string? ChooseApplication() => MacNative.ReadString(MacNative.ChooseApplication());
    public static bool ScreenPermission(bool request = false) => MacNative.ScreenPermission(request);
    public static (double X, double Y) MousePosition() => (MacNative.MouseX(), MacNative.MouseY());
    public static byte[] CapturePng(uint id)
    {
        var pointer = MacNative.CapturePng(id, out var count, out var error);
        var message = MacNative.ReadString(error);
        if (pointer == 0) throw new InvalidOperationException(message ?? "ScreenCaptureKit 截图失败");
        try
        {
            var bytes = new byte[count];
            Marshal.Copy(pointer, bytes, 0, count);
            return bytes;
        }
        finally { MacNative.Free(pointer); }
    }
    public Task<WindowCatalogResult> EnumerateAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = ReadWindows().Select(w => new WindowCandidate(new($"mac:{w.Id}"), w.Pid,
            w.Owner, w.Title, "", StartedAt(w.Pid), w.Visible, w.Minimized,
            false, false, false, w.Self, null,
            BundleId: EmptyToNull(w.BundleId), AppBundlePath: EmptyToNull(w.BundlePath), Platform: "macOS")).ToArray();
        return new WindowCatalogResult(result);
    }, cancellationToken);
    public Task<WindowReadResult> ReadAsync(WindowIdentity identity, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!identity.Id.Value.StartsWith("mac:") || !uint.TryParse(identity.Id.Value.AsSpan(4), out var id))
            return Lost(identity, IdentityVerification.Mismatch, "窗口 ID 不是 macOS WindowID");
        var window = ReadWindows().FirstOrDefault(w => w.Id == id);
        if (window is null) return Lost(identity, IdentityVerification.Destroyed, "窗口已消失");
        if (window.Pid != identity.ProcessId) return Lost(identity, IdentityVerification.Mismatch, "窗口所属进程变化");
        var started = StartedAt(window.Pid);
        if (identity.ProcessStartedAt is not null && started != identity.ProcessStartedAt)
            return Lost(identity, IdentityVerification.Mismatch, "进程启动时间变化");
        var updated = identity with { ProcessStartedAt = started,
            Verification = started is null ? IdentityVerification.Insufficient : IdentityVerification.Verified };
        if (!window.Visible || window.Minimized)
            return new(updated, null, false, true, new(WindowErrorCode.ApiFailure, "窗口不可见或已最小化"));
        var placement = new WindowPlacement(DesktopCoordinateSpace.MacDesktopPoints,
            new(window.X, window.Y, window.Width, window.Height),
            window.WorkWidth > 0 ? new(window.WorkX, window.WorkY, window.WorkWidth, window.WorkHeight) : null,
            window.DisplayId, window.PixelsPerPoint > 0 ? window.PixelsPerPoint : null, false);
        var empty = new ScreenPixelRect();
        var geometry = new WindowGeometrySnapshot(empty, empty, VisibleFrameSource.WindowBoundsFallback,
            new(0, 0), new(0, 0), empty, empty, empty, "macOS", 0,
            DpiAwarenessKind.Unknown, DpiAwarenessKind.Unknown, GeometryValidity.Valid,
            DateTimeOffset.UtcNow, placement);
        return new(updated, geometry, window.Foreground, false);
    }, cancellationToken);
    private static DateTimeOffset? StartedAt(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return process.StartTime.ToUniversalTime(); }
        catch { return null; }
    }
    private static string? EmptyToNull(string text) => string.IsNullOrWhiteSpace(text) ? null : text;
    private static WindowReadResult Lost(WindowIdentity identity, IdentityVerification verification, string text) =>
        new(identity with { Verification = verification }, null, false, false,
            new(WindowErrorCode.WindowDestroyed, text));
}
