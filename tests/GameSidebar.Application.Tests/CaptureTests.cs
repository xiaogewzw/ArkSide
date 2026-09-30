using GameSidebar.Application.Abstractions;
using GameSidebar.Application.Capture;
using GameSidebar.Application.Sessions;
using GameSidebar.Core.Geometry;
using GameSidebar.Core.Profiles;
using GameSidebar.Core.Sessions;
using GameSidebar.App.Development;
using SkiaSharp;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameSidebar.Application.Tests;

public sealed class CaptureTests
{
    [Fact]
    public async Task Mac_frame_can_preview_without_a_client_profile_mapping()
    {
        await using var manager = new GameSessionManager(new EmptyCatalog(), new MacProvider(), new Profiles(),
            NullLogger<GameSessionManager>.Instance);
        await manager.InitializeAsync(new());
        await manager.BindAsync(Candidate("mac:42"));
        Assert.Equal(GameSessionState.PreviewOnly, manager.Current.State);
        Assert.True(manager.Current.CanPreview);
        Assert.Equal(ProfileValidationKind.GeometryUnavailable, manager.Current.ProfileValidation?.Kind);
    }
    [Fact]
    public async Task Demo_backend_produces_a_decodable_png()
    {
        var backend = new FakeCaptureBackend();
        var payload = await backend.CaptureAsync(new(new("demo:0:0"), 5000, null, IdentityVerification.Verified),
            CancellationToken.None);
        using var bitmap = SKBitmap.Decode(payload.Png);
        Assert.NotNull(bitmap);
        Assert.Equal(640, bitmap.Width);
        Assert.Equal(360, bitmap.Height);
    }
    [Fact]
    public async Task Rebinding_discards_an_in_flight_frame()
    {
        await using var manager = Manager();
        await manager.InitializeAsync(new());
        await manager.BindAsync(Candidate("first"));
        var backend = new DelayedBackend();
        await using var capture = new CaptureCoordinator(manager, backend);
        var pending = capture.CaptureOnceAsync();
        await backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await manager.BindAsync(Candidate("second"));
        backend.Release.TrySetResult();
        var result = await pending;
        Assert.Null(result.Frame);
        Assert.Equal(WindowErrorCode.Cancelled, result.Error?.Code);
    }

    [Fact]
    public async Task Stopping_preview_prevents_any_more_capture_requests()
    {
        await using var manager = Manager();
        await manager.InitializeAsync(new());
        await manager.BindAsync(Candidate("first"));
        var backend = new CountingBackend();
        await using var capture = new CaptureCoordinator(manager, backend);
        capture.StartPreview(5);
        await backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await capture.StopPreviewAsync();
        var count = backend.Count;
        await Task.Delay(300);
        Assert.Equal(count, backend.Count);
        await capture.DisposeAsync();
        await capture.DisposeAsync(); // Host and window shutdown can both dispose the coordinator.
    }

    [Fact]
    public async Task Stop_waits_for_native_work_and_restart_cannot_replace_that_work()
    {
        await using var manager = Manager();
        await manager.InitializeAsync(new());
        await manager.BindAsync(Candidate("first"));
        var backend = new DelayedBackend();
        await using var capture = new CaptureCoordinator(manager, backend);
        capture.StartPreview(5);
        await backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stopping = capture.StopPreviewAsync();
        Assert.False(stopping.IsCompleted);
        capture.StartPreview(5);
        Assert.True(capture.IsPreviewing);
        backend.Release.TrySetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(capture.IsPreviewing);
        await capture.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => capture.StartPreview());
    }

    [Fact]
    public async Task Unbinding_releases_controller_after_in_flight_work_finishes()
    {
        await using var manager = Manager();
        await manager.InitializeAsync(new());
        await manager.BindAsync(Candidate("first"));
        var backend = new DelayedBackend();
        await using var capture = new CaptureCoordinator(manager, backend);
        var pending = capture.CaptureOnceAsync();
        await backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await manager.UnbindAsync();
        Assert.Equal(1, backend.ResetCount);
        backend.Release.TrySetResult();
        Assert.Null((await pending).Frame);
        await backend.UnboundReset.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, backend.ResetCount);
    }

    private static GameSessionManager Manager() => new(new EmptyCatalog(), new Provider(), new Profiles(),
        NullLogger<GameSessionManager>.Instance);
    private static WindowCandidate Candidate(string id) => new(new(id), 1024, "game.exe", id, "Test",
        DateTimeOffset.UnixEpoch, true, false, false, false, false, false, null);
    private static readonly byte[] PngHeader =
        [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 1, 0, 0, 0, 1];
    private sealed class DelayedBackend : IGameCaptureBackend
    {
        public int ResetCount;
        public TaskCompletionSource UnboundReset { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<CapturePayload> CaptureAsync(WindowIdentity identity, CancellationToken token)
        { Started.TrySetResult(); await Release.Task; return new(PngHeader, "test"); }
        public Task ResetAsync()
        {
            if (Interlocked.Increment(ref ResetCount) == 2) UnboundReset.TrySetResult();
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class CountingBackend : IGameCaptureBackend
    {
        public int Count;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<CapturePayload> CaptureAsync(WindowIdentity identity, CancellationToken token)
        { Interlocked.Increment(ref Count); Started.TrySetResult(); return Task.FromResult(new CapturePayload(PngHeader, "test")); }
        public Task ResetAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class EmptyCatalog : IWindowCatalog
    { public Task<WindowCatalogResult> EnumerateAsync(CancellationToken token) => Task.FromResult(new WindowCatalogResult([])); }
    private sealed class Profiles : IProfileRepository
    { public Task<ProfileLoadResult> LoadAsync(CancellationToken token) => Task.FromResult(new ProfileLoadResult(
        [new GameWindowProfile(1, "test", ProfilePurpose.DiagnosticOnly, null, new(100, 100), new(360, 48))])); }
    private sealed class Provider : IWindowGeometryProvider
    {
        public Task<WindowReadResult> ReadAsync(WindowIdentity identity, CancellationToken token) => Task.FromResult(
            new WindowReadResult(identity with { Verification = IdentityVerification.Verified },
                new WindowGeometrySnapshot(new(0, 0, 100, 100), new(0, 0, 100, 100),
                    VisibleFrameSource.Dwm, new(100, 100), new(0, 0), new(0, 0, 100, 100),
                    new(0, 0, 1000, 1000), new(0, 0, 1000, 1000), "test", 96,
                    DpiAwarenessKind.PerMonitorAwareV2, DpiAwarenessKind.PerMonitorAwareV2,
                    GeometryValidity.Valid, DateTimeOffset.UtcNow), true, false));
    }
    private sealed class MacProvider : IWindowGeometryProvider
    {
        public Task<WindowReadResult> ReadAsync(WindowIdentity identity, CancellationToken token) => Task.FromResult(
            new WindowReadResult(identity with { Verification = IdentityVerification.Verified },
                new WindowGeometrySnapshot(default, default, VisibleFrameSource.WindowBoundsFallback,
                    new(0, 0), default, default, default, default, "mac", 0,
                    DpiAwarenessKind.Unknown, DpiAwarenessKind.Unknown, GeometryValidity.Valid,
                    DateTimeOffset.UtcNow, new(DesktopCoordinateSpace.MacDesktopPoints,
                        new(100, 100, 800, 600), null, "display", 2, false)), false, false));
    }
}
