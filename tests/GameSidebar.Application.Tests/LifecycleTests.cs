using System.Threading.Channels;
using GameSidebar.Application.Abstractions;
using GameSidebar.Application.Sessions;
using GameSidebar.Core.Geometry;
using GameSidebar.Core.Profiles;
using GameSidebar.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameSidebar.Application.Tests;

public sealed class LifecycleTests
{
    private static readonly WindowCandidate Candidate = new(new("window"), 7300, "test.exe", "test", "Test",
        DateTimeOffset.UnixEpoch, true, false, false, false, false, false, null);
    private static readonly GameWindowProfile Profile = new(1, "diagnostic", ProfilePurpose.DiagnosticOnly,
        null, new(1920, 1080), new(360, 48));

    [Fact]
    public async Task Size_minimize_and_move_revalidate_without_rebinding()
    {
        var time = new ManualTimeProvider();
        var provider = new ControlledTrackingProvider(Candidate);
        await using var manager = new GameSessionManager(new Catalog(), provider, new Profiles(),
            NullLogger<GameSessionManager>.Instance, time);
        await manager.InitializeAsync(new());
        await manager.BindAsync(Candidate);
        var session = manager.Current.SessionId;
        Assert.Equal(GameSessionState.Ready, manager.Current.State);
        Assert.Equal(1, manager.Current.GeometryVersion);

        var unsupported = await NextRead(provider, time);
        unsupported.SetResult(Result(Candidate, 1280, 720));
        await WaitFor(manager, s => s.State == GameSessionState.UnsupportedResolution);
        Assert.Equal(session, manager.Current.SessionId);
        Assert.Equal(2, manager.Current.GeometryVersion);

        var minimized = await NextRead(provider, time);
        minimized.SetResult(new(new(Candidate.Id, Candidate.ProcessId, Candidate.ProcessStartedAt,
            IdentityVerification.Verified), null, false, true,
            new(WindowErrorCode.ApiFailure, "已最小化")));
        await WaitFor(manager, s => s.State == GameSessionState.BoundUnavailable);
        Assert.Equal(GeometryValidity.Stale, manager.Current.Geometry?.Validity);
        Assert.NotEqual(ProfileValidationKind.UnsupportedResolution, manager.Current.ProfileValidation?.Kind);

        var restored = await NextRead(provider, time);
        restored.SetResult(Result(Candidate, x: -250));
        await WaitFor(manager, s => s.State == GameSessionState.Ready && s.Geometry?.ClientOriginScreenPx.X == -242);
        Assert.Equal(session, manager.Current.SessionId);
        Assert.Equal(3, manager.Current.GeometryVersion);

        var timestampOnly = await NextRead(provider, time);
        timestampOnly.SetResult(Result(Candidate, x: -250));
        await WaitFor(manager, s => s.ObservedAt > DateTimeOffset.UnixEpoch.AddMilliseconds(600));
        Assert.Equal(3, manager.Current.GeometryVersion);
    }

    [Fact]
    public async Task Stopping_discovery_preserves_tracking_but_prevents_rebind_after_loss()
    {
        var time = new ManualTimeProvider();
        var catalog = new Catalog([Candidate]);
        var provider = new ControlledTrackingProvider(Candidate);
        await using var manager = new GameSessionManager(catalog, provider, new Profiles(),
            NullLogger<GameSessionManager>.Instance, time);
        await manager.InitializeAsync(new(Exe: "test.exe"));
        await WaitFor(manager, s => s.State == GameSessionState.Ready);
        var session = manager.Current.SessionId;
        await manager.StopDiscoveryAsync();
        Assert.Equal(session, manager.Current.SessionId);
        Assert.False(manager.Current.AutoDiscoveryEnabled);

        var lostRead = await NextRead(provider, time, TimeSpan.FromSeconds(1));
        lostRead.SetResult(Result(Candidate, status: IdentityVerification.Mismatch));
        await WaitFor(manager, s => s.State == GameSessionState.Unbound);
        Assert.Null(manager.Current.SessionId);
        Assert.False(manager.Current.AutoDiscoveryEnabled);
        var calls = catalog.Calls;
        time.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(calls, catalog.Calls);
    }

    private static async Task<TaskCompletionSource<WindowReadResult>> NextRead(ControlledTrackingProvider provider,
        ManualTimeProvider time, TimeSpan? interval = null)
    {
        var next = provider.NextTrackingReadAsync();
        var eventTask = await Task.WhenAny(next, time.TimerCreated).WaitAsync(TimeSpan.FromSeconds(5));
        if (eventTask != next) time.Advance(interval ?? TimeSpan.FromMilliseconds(200));
        return await next.WaitAsync(TimeSpan.FromSeconds(5));
    }
    private static async Task WaitFor(GameSessionManager manager, Func<GameSessionSnapshot, bool> predicate)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Updated(GameSessionSnapshot snapshot) { if (predicate(snapshot)) signal.TrySetResult(); }
        manager.Updated += Updated;
        if (predicate(manager.Current)) signal.TrySetResult();
        try { await signal.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { manager.Updated -= Updated; }
    }
    private static WindowReadResult Result(WindowCandidate candidate, int width = 1920, int height = 1080,
        int x = 100, IdentityVerification status = IdentityVerification.Verified)
    {
        var geometry = new WindowGeometrySnapshot(new(x, 80, x + width + 16, 119 + height),
            new(x + 8, 88, x + width + 8, 111 + height), VisibleFrameSource.Dwm,
            new(width, height), new(x + 8, 111), new(x + 8, 111, x + 8 + width, 111 + height),
            new(-1920, 0, 1920, 1080), new(-1920, 0, 1920, 1040), "monitor", 96,
            DpiAwarenessKind.PerMonitorAwareV2, DpiAwarenessKind.PerMonitorAwareV2,
            GeometryValidity.Valid, DateTimeOffset.UtcNow);
        return new(new(candidate.Id, candidate.ProcessId, candidate.ProcessStartedAt, status), geometry, true, false);
    }
    private sealed class ControlledTrackingProvider(WindowCandidate candidate) : IWindowGeometryProvider
    {
        private readonly Channel<TaskCompletionSource<WindowReadResult>> _reads = Channel.CreateUnbounded<TaskCompletionSource<WindowReadResult>>();
        private int _count;
        public Task<WindowReadResult> ReadAsync(WindowIdentity identity, CancellationToken token)
        {
            if (Interlocked.Increment(ref _count) == 1) return Task.FromResult(Result(candidate));
            var completion = new TaskCompletionSource<WindowReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _reads.Writer.TryWrite(completion);
            return completion.Task;
        }
        public Task<TaskCompletionSource<WindowReadResult>> NextTrackingReadAsync() =>
            _reads.Reader.ReadAsync().AsTask();
    }
    private sealed class Catalog(IReadOnlyList<WindowCandidate>? candidates = null) : IWindowCatalog
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public Task<WindowCatalogResult> EnumerateAsync(CancellationToken token)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new WindowCatalogResult(candidates ?? []));
        }
    }
    private sealed class Profiles : IProfileRepository
    {
        public Task<ProfileLoadResult> LoadAsync(CancellationToken token) =>
            Task.FromResult(new ProfileLoadResult([Profile]));
    }
}
