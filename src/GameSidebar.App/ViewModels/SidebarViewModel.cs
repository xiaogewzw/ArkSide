using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSidebar.Application.Capture;
using GameSidebar.Application.Sessions;
using GameSidebar.Core.Sessions;
using GameSidebar.Platform.MacOS;
using System.Runtime.InteropServices;

namespace GameSidebar.App.ViewModels;

public sealed class SidebarViewModel : ObservableObject, IDisposable
{
    private readonly CaptureCoordinator _capture;
    private readonly GameSessionManager _sessions;
    private Bitmap? _image;
    private byte[]? _png;
    private string _status = "绑定窗口后可截图";
    private bool _collapsed;
    private int _fps = 2;
    private long _lastSequence;
    private CaptureOutcome? _pendingOutcome;
    private int _dispatchQueued;
    private bool _disposed;
    private long _displayGeneration = -1;
    public SidebarViewModel(CaptureCoordinator capture, GameSessionManager sessions)
    {
        _capture = capture; _sessions = sessions;
        _capture.Updated += OnCapture;
        _sessions.Updated += OnSession;
        ScreenshotCommand = new AsyncRelayCommand(ScreenshotAsync);
        StartPreviewCommand = new RelayCommand(StartPreview);
        StopPreviewCommand = new AsyncRelayCommand(StopPreviewAsync);
        ToggleCommand = new RelayCommand(() => Collapsed = !Collapsed);
        RequestPermissionCommand = new RelayCommand(RequestPermission);
    }
    public Bitmap? Image { get => _image; private set => SetProperty(ref _image, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool Collapsed { get => _collapsed; set => SetProperty(ref _collapsed, value); }
    public int Fps { get => _fps; set => SetProperty(ref _fps, Math.Clamp(value, 1, 5)); }
    public bool IsMac => OperatingSystem.IsMacOS();
    public IAsyncRelayCommand ScreenshotCommand { get; }
    public IRelayCommand StartPreviewCommand { get; }
    public IAsyncRelayCommand StopPreviewCommand { get; }
    public IRelayCommand ToggleCommand { get; }
    public IRelayCommand RequestPermissionCommand { get; }

    private async Task ScreenshotAsync()
    {
        try { Apply(await _capture.CaptureOnceAsync()); }
        catch (Exception e) { Status = e.Message; }
    }
    private void StartPreview()
    {
        _capture.StartPreview(Fps);
        Status = $"预览运行中 · {Fps} FPS";
    }
    private async Task StopPreviewAsync()
    {
        await _capture.StopPreviewAsync();
        Status = "预览已停止";
    }
    private void OnCapture(CaptureOutcome outcome)
    {
        if (_disposed) return;
        Interlocked.Exchange(ref _pendingOutcome, outcome);
        if (Interlocked.Exchange(ref _dispatchQueued, 1) == 0)
            Dispatcher.UIThread.Post(DrainLatest);
    }
    private void DrainLatest()
    {
        var latest = Interlocked.Exchange(ref _pendingOutcome, null);
        if (latest is not null) Apply(latest);
        Interlocked.Exchange(ref _dispatchQueued, 0);
        if (_pendingOutcome is not null && Interlocked.Exchange(ref _dispatchQueued, 1) == 0)
            Dispatcher.UIThread.Post(DrainLatest);
    }
    private void Apply(CaptureOutcome outcome)
    {
        if (_disposed) return;
        if (outcome.Error is not null) { Status = outcome.Error.Message; return; }
        var frame = outcome.Frame!;
        var current = _sessions.Current;
        if (!current.CanPreview || current.SessionId != frame.SessionId ||
            current.BindingGeneration != frame.BindingGeneration || current.GeometryVersion != frame.GeometryVersion ||
            frame.Sequence <= _lastSequence) return;
        _lastSequence = frame.Sequence;
        _png = frame.Png;
        using var stream = new MemoryStream(frame.Png, writable: false);
        var bitmap = new Bitmap(stream);
        var previous = Image;
        Image = bitmap;
        previous?.Dispose();
        Status = $"{frame.Method} · {frame.Width}×{frame.Height} · #{frame.Sequence} · " +
            $"{frame.CapturedAt.ToLocalTime():HH:mm:ss.fff} · {frame.Duration.TotalMilliseconds:F0} ms · " +
            PointerText(frame);
    }
    private string PointerText(GameFrame frame)
    {
        var geometry = _sessions.Current.Geometry;
        if (geometry is null) return "鼠标映射未知";
        if (OperatingSystem.IsMacOS())
        {
            var (x, y) = MacWindowService.MousePosition();
            var bounds = geometry.Placement?.Frame;
            var inside = bounds is not null && x >= bounds.X && x < bounds.X + bounds.Width &&
                y >= bounds.Y && y < bounds.Y + bounds.Height;
            return $"鼠标桌面 ({x:F0},{y:F0}) point · {(inside ? "窗口外框内" : "窗口外框外")} · 帧内映射未知";
        }
        if (OperatingSystem.IsWindows() && GetCursorPos(out var point))
        {
            var client = geometry.ClientBoundsScreenPx;
            var inside = point.X >= client.Left && point.X < client.Right &&
                point.Y >= client.Top && point.Y < client.Bottom;
            if (inside && frame.Width == geometry.ClientSizePx.Width && frame.Height == geometry.ClientSizePx.Height)
                return $"鼠标帧内 ({point.X - client.Left},{point.Y - client.Top}) px";
            return $"鼠标桌面 ({point.X},{point.Y}) px · 帧内映射未知";
        }
        return "鼠标映射未知";
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);
    private void OnSession(GameSessionSnapshot snapshot) => Dispatcher.UIThread.Post(() =>
    {
        if (_disposed) return;
        if (_displayGeneration != snapshot.BindingGeneration || snapshot.SessionId is null || !snapshot.CanPreview)
        {
            _displayGeneration = snapshot.BindingGeneration;
            _png = null;
            var previous = Image; Image = null; previous?.Dispose();
            if (snapshot.IsMinimized) Status = "目标最小化，截图已暂停";
            else if (snapshot.SessionId is null) Status = "目标已解绑，等待重新绑定";
            else Status = "目标已变更，请重新截图";
        }
    });
    public async Task SavePngAsync(string path)
    {
        if (_png is null) { Status = "请先截图"; return; }
        await File.WriteAllBytesAsync(path, _png);
        Status = $"已保存 PNG：{path}";
    }
    private void RequestPermission()
    {
        if (!IsMac) return;
        Status = MacWindowService.ScreenPermission(true) ? "屏幕录制权限已允许，可重试截图" :
            "请在系统设置 → 隐私与安全性 → 屏幕录制中允许 GameSidebar，然后重试";
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Exchange(ref _pendingOutcome, null);
        _capture.Updated -= OnCapture;
        _sessions.Updated -= OnSession;
        _image?.Dispose();
    }
}
