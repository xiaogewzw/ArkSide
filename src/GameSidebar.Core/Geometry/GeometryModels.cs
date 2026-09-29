namespace GameSidebar.Core.Geometry;

public readonly record struct ScreenPixelPoint(int X, int Y);
public readonly record struct ClientPixelPoint(int X, int Y);
public readonly record struct ClientPixelSize(int Width, int Height)
{
    public bool IsValid => Width > 0 && Height > 0;
}
public readonly record struct ScreenPixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsValid => Width >= 0 && Height >= 0;
}
public enum GeometryValidity { Valid, Minimized, Unavailable, Inconsistent, Stale }
public enum VisibleFrameSource { Dwm, WindowBoundsFallback }
public enum DpiAwarenessKind { Unknown, Unaware, SystemAware, PerMonitorAware, PerMonitorAwareV2 }

public sealed record WindowGeometrySnapshot(
    ScreenPixelRect WindowBoundsPx,
    ScreenPixelRect VisibleFrameBoundsPx,
    VisibleFrameSource VisibleFrameSource,
    ClientPixelSize ClientSizePx,
    ScreenPixelPoint ClientOriginScreenPx,
    ScreenPixelRect ClientBoundsScreenPx,
    ScreenPixelRect MonitorBoundsPx,
    ScreenPixelRect WorkAreaPx,
    string MonitorId,
    uint TargetWindowDpi,
    DpiAwarenessKind TargetAwareness,
    DpiAwarenessKind CallerAwareness,
    GeometryValidity Validity,
    DateTimeOffset ObservedAt)
{
    public bool IsUsable => Validity == GeometryValidity.Valid && ClientSizePx.IsValid;
    public bool SameMapping(WindowGeometrySnapshot other) =>
        WindowBoundsPx == other.WindowBoundsPx && VisibleFrameBoundsPx == other.VisibleFrameBoundsPx &&
        ClientSizePx == other.ClientSizePx && ClientOriginScreenPx == other.ClientOriginScreenPx &&
        MonitorBoundsPx == other.MonitorBoundsPx && WorkAreaPx == other.WorkAreaPx &&
        MonitorId == other.MonitorId && TargetWindowDpi == other.TargetWindowDpi &&
        TargetAwareness == other.TargetAwareness && CallerAwareness == other.CallerAwareness;
}
