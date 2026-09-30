using System.Diagnostics;
using GameSidebar.Application.Sessions;
using GameSidebar.Core.Sessions;

namespace GameSidebar.Application.Capture;

public sealed record CapturePayload(byte[] Png, string Method);
public sealed record GameFrame(Guid SessionId, long BindingGeneration, long GeometryVersion,
    long Sequence, int Width, int Height, byte[] Png, string Method,
    DateTimeOffset CapturedAt, TimeSpan Duration);
public sealed record CaptureOutcome(GameFrame? Frame, WindowOperationError? Error);
public sealed class GameCaptureException(WindowErrorCode code, string message) : Exception(message)
{ public WindowErrorCode Code { get; } = code; }

public interface IGameCaptureBackend : IAsyncDisposable
{
    Task<CapturePayload> CaptureAsync(WindowIdentity identity, CancellationToken token);
    Task ResetAsync();
}

public sealed class CaptureCoordinator : IAsyncDisposable
{
    private readonly GameSessionManager _sessions;
    private readonly IGameCaptureBackend _backend;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _previewStop;
    private Task? _previewTask;
    private readonly object _previewLock = new();
    private long _sequence;
    private long _backendGeneration = -1;
    private long _observedGeneration = -1;
    private bool _disposed;
    public event Action<CaptureOutcome>? Updated;
    public bool IsPreviewing { get { lock (_previewLock) return _previewStop is not null; } }

    public CaptureCoordinator(GameSessionManager sessions, IGameCaptureBackend backend)
    { _sessions = sessions; _backend = backend; _sessions.Updated += OnSessionUpdated; }

    public async Task<CaptureOutcome> CaptureOnceAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var before = _sessions.Current;
            if (!before.CanPreview || before.SessionId is null || before.Identity is null)
                return new(null, new(WindowErrorCode.WindowDestroyed, "目标窗口不可预览；请检查绑定、可见性和身份"));
            if (_backendGeneration != before.BindingGeneration)
            {
                await _backend.ResetAsync();
                _backendGeneration = before.BindingGeneration;
            }
            var watch = Stopwatch.StartNew();
            CapturePayload payload;
            try { payload = await _backend.CaptureAsync(before.Identity, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (UnauthorizedAccessException e) { return new(null, new(WindowErrorCode.PermissionDenied, e.Message)); }
            catch (DllNotFoundException e) { return new(null, new(WindowErrorCode.NativeLoadFailure, $"Native 加载失败：{e.Message}")); }
            catch (GameCaptureException e) { return new(null, new(e.Code, e.Message)); }
            catch (Exception e) { return new(null, new(WindowErrorCode.CaptureFailed, $"截图失败：{e.Message}")); }
            var now = _sessions.Current;
            if (!now.CanPreview || now.SessionId != before.SessionId || now.BindingGeneration != before.BindingGeneration ||
                now.GeometryVersion != before.GeometryVersion ||
                now.Identity != before.Identity)
                return new(null, new(WindowErrorCode.Cancelled, "换绑或目标丢失，已丢弃旧帧"));
            int width, height;
            try { (width, height) = PngDimensions(payload.Png); }
            catch (InvalidDataException e) { return new(null, new(WindowErrorCode.CaptureFailed, e.Message)); }
            return new(new(before.SessionId.Value, before.BindingGeneration, before.GeometryVersion,
                Interlocked.Increment(ref _sequence), width, height, payload.Png, payload.Method,
                DateTimeOffset.UtcNow, watch.Elapsed), null);
        }
        finally { _gate.Release(); }
    }

    private void OnSessionUpdated(GameSessionSnapshot snapshot)
    {
        if (Interlocked.Exchange(ref _observedGeneration, snapshot.BindingGeneration) != snapshot.BindingGeneration &&
            snapshot.SessionId is null && !_disposed)
            _ = ReleaseWhenUnboundAsync();
    }
    private async Task ReleaseWhenUnboundAsync()
    {
        try
        {
            await _gate.WaitAsync();
            try
            {
                if (!_disposed && _sessions.Current.SessionId is null)
                { await _backend.ResetAsync(); _backendGeneration = -1; }
            }
            finally { _gate.Release(); }
        }
        catch (ObjectDisposedException) { }
    }

    public void StartPreview(int fps = 2)
    {
        if (fps is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(fps));
        lock (_previewLock)
        {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_previewStop is not null) return;
        var stop = new CancellationTokenSource();
        _previewStop = stop;
        _previewTask = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    if (_sessions.Current.CanPreview)
                    {
                        var outcome = await CaptureOnceAsync(stop.Token);
                        if (!stop.IsCancellationRequested && outcome.Error?.Code != WindowErrorCode.Cancelled)
                            Updated?.Invoke(outcome);
                    }
                    await Task.Delay(TimeSpan.FromSeconds(1d / fps), stop.Token);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            }
        });
        }
    }

    public async Task StopPreviewAsync()
    {
        CancellationTokenSource stop;
        Task task;
        lock (_previewLock)
        {
            if (_previewStop is null) return;
            stop = _previewStop;
            task = _previewTask!;
            stop.Cancel();
        }
        try { await task; }
        finally
        {
            lock (_previewLock)
            {
                if (ReferenceEquals(_previewStop, stop))
                { _previewStop = null; _previewTask = null; stop.Dispose(); }
            }
        }
    }

    private static (int Width, int Height) PngDimensions(byte[] png)
    {
        if (png.Length < 24 || png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71)
            throw new InvalidDataException("截图不是有效 PNG");
        return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _sessions.Updated -= OnSessionUpdated;
        await StopPreviewAsync();
        await _gate.WaitAsync();
        try { await _backend.ResetAsync(); await _backend.DisposeAsync(); }
        finally { _gate.Release(); _gate.Dispose(); }
    }
}
