using GameSidebar.Application.Capture;
using GameSidebar.Core.Sessions;
using GameSidebar.Platform.MacOS;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;

namespace GameSidebar.Infrastructure.Maa;

public sealed class MaaCaptureBackend : IGameCaptureBackend
{
    private MaaController? _controller;
    private WindowId? _windowId;
    private bool _macSystemFallback;

    public Task<CapturePayload> CaptureAsync(WindowIdentity identity, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (OperatingSystem.IsMacOS() && !MacWindowService.ScreenPermission())
            throw new UnauthorizedAccessException("缺少屏幕录制权限；请在系统设置授权后重试");
        if (_windowId != identity.Id)
        {
            _controller?.Dispose();
            _controller = null;
            _windowId = identity.Id;
            _macSystemFallback = false;
        }
        if (_macSystemFallback) return CaptureMacSystem(identity, token);
        if (_controller is null)
        {
            try { _controller = Create(identity.Id); }
            catch when (OperatingSystem.IsMacOS()) { return CaptureMacFallback(identity, token); }
        }
        byte[]? png;
        try
        {
            using var image = new MaaImageBuffer();
            if (!_controller.Screencap().Wait().IsSucceeded() || !_controller.GetCachedImage(image) ||
                !image.TryGetEncodedData(out png) || png.Length == 0)
                throw new InvalidOperationException("Maa Controller 未返回截图");
        }
        catch when (OperatingSystem.IsMacOS()) { return CaptureMacFallback(identity, token); }
        token.ThrowIfCancellationRequested();
        return new CapturePayload(png!, OperatingSystem.IsMacOS() ? "Maa ScreenCaptureKit" : "Maa FramePool/PrintWindow");
    }, token);

    private CapturePayload CaptureMacFallback(WindowIdentity identity, CancellationToken token)
    {
        _controller?.Dispose(); _controller = null;
        if (!uint.TryParse(identity.Id.Value.AsSpan(4), out var id)) throw new InvalidOperationException("无效的 Mac WindowID");
        try { return new(MacWindowService.CapturePng(id), "ScreenCaptureKit bridge"); }
        catch { _macSystemFallback = true; return CaptureMacSystem(identity, token); }
    }

    private static CapturePayload CaptureMacSystem(WindowIdentity identity, CancellationToken token)
    {
        if (!uint.TryParse(identity.Id.Value.AsSpan(4), out var id)) throw new InvalidOperationException("无效的 Mac WindowID");
        VerifyMacWindow(identity, id);
        var path = Path.Combine(Path.GetTempPath(), $"gamesidebar-{Guid.NewGuid():N}.png");
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new("/usr/sbin/screencapture") { UseShellExecute = false, CreateNoWindow = true };
            process.StartInfo.ArgumentList.Add("-x");
            process.StartInfo.ArgumentList.Add("-l");
            process.StartInfo.ArgumentList.Add(id.ToString());
            process.StartInfo.ArgumentList.Add(path);
            if (!process.Start()) throw new InvalidOperationException("无法启动系统截图工具");
            using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(); } catch { } });
            process.WaitForExit();
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0 || !File.Exists(path)) throw new GameCaptureException(WindowErrorCode.CaptureFailed,
                $"系统截图失败，退出码 {process.ExitCode}；请检查 GameSidebar 的屏幕录制权限");
            VerifyMacWindow(identity, id);
            return new(File.ReadAllBytes(path), "macOS system screencapture");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void VerifyMacWindow(WindowIdentity identity, uint id)
    {
        var current = MacWindowService.ReadWindows().FirstOrDefault(w => w.Id == id);
        if (current is null || current.Pid != identity.ProcessId)
            throw new GameCaptureException(WindowErrorCode.WindowDestroyed, "截图期间窗口身份变化");
    }

    private static MaaController Create(WindowId id)
    {
        if (OperatingSystem.IsMacOS() && id.Value.StartsWith("mac:") &&
            uint.TryParse(id.Value.AsSpan(4), out var macId))
            return new MaaMacOSController(macId, MacOSScreencapMethod.ScreenCaptureKit, MacOSInputMethod.None);
        if (OperatingSystem.IsWindows() && id.Value.StartsWith("0x") &&
            long.TryParse(id.Value.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out var hwnd))
            return new MaaWin32Controller(new nint(hwnd), Win32ScreencapMethods.FramePool | Win32ScreencapMethods.PrintWindow,
                Win32InputMethod.None, Win32InputMethod.None);
        throw new InvalidOperationException($"不支持的窗口 ID：{id}");
    }

    public Task ResetAsync()
    {
        _controller?.Dispose();
        _controller = null;
        _windowId = null;
        _macSystemFallback = false;
        return Task.CompletedTask;
    }
    public async ValueTask DisposeAsync() => await ResetAsync();
}
