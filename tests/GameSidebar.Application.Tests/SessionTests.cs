using GameSidebar.Application.Abstractions;
using GameSidebar.Application.Discovery;
using GameSidebar.Application.Sessions;
using GameSidebar.Core.Geometry;
using GameSidebar.Core.Profiles;
using GameSidebar.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameSidebar.Application.Tests;

public sealed class SessionTests
{
    private static readonly GameWindowProfile Profile = new(1, "diagnostic", ProfilePurpose.DiagnosticOnly, null, new(1920, 1080), new(360, 48));
    private static WindowCandidate Candidate(string id, int pid = 1) => new(new(id), pid, "test.exe", id, "Test", DateTimeOffset.UnixEpoch,
        true, false, false, false, false, false, null);
    private static WindowReadResult Read(WindowCandidate candidate, IdentityVerification status = IdentityVerification.Verified,
        ClientPixelSize? size = null) => new(new(candidate.Id, candidate.ProcessId, candidate.ProcessStartedAt, status),
        new(new(-100, 0, 1836, 1119), new(-92, 8, 1828, 1111), VisibleFrameSource.Dwm,
            size ?? new(1920, 1080), new(-92, 31), new(-92, 31, 1828, 1111),
            new(-1920, 0, 1920, 1080), new(-1920, 0, 1920, 1040), "monitor", 96,
            DpiAwarenessKind.PerMonitorAwareV2, DpiAwarenessKind.PerMonitorAwareV2,
            GeometryValidity.Valid, DateTimeOffset.UtcNow), true, false);
    private static GameSessionManager Manager(IWindowGeometryProvider provider) => new(new FakeCatalog(), provider,
        new FakeProfiles(), NullLogger<GameSessionManager>.Instance);

    [Fact] public void Match_service_does_not_select_first_of_multiple_candidates()
    {
        var result = WindowMatchService.Match(new(Exe: "TEST.EXE"), [Candidate("a"), Candidate("b")]);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Empty(WindowMatchService.Match(new(), [Candidate("a")]).Candidates);
    }
    [Fact] public async Task Late_A_result_cannot_replace_B()
    {
        var a = Candidate("a"); var b = Candidate("b", 2);
        var provider = new ControlledProvider();
        await using var manager = Manager(provider);
        await manager.InitializeAsync(new());
        var bindA = manager.BindAsync(a);
        await provider.ARequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(await manager.BindAsync(b));
        provider.CompleteA(Read(a));
        await bindA;
        Assert.Equal(b.Id, manager.Current.Identity?.Id);
        Assert.Equal(GameSessionState.Ready, manager.Current.State);
        Assert.Equal(2, manager.Current.BindingGeneration);
    }
    [Fact] public async Task Manual_unbind_disables_automatic_rebind_and_stops_tracking()
    {
        var candidate = Candidate("b");
        await using var manager = Manager(new ImmediateProvider(candidate));
        await manager.InitializeAsync(new(Exe: "test.exe"));
        await manager.StartDiscoveryAsync();
        await manager.BindAsync(candidate);
        await manager.StopDiscoveryAsync();
        Assert.Equal(GameSessionState.Ready, manager.Current.State);
        Assert.False(manager.Current.AutoDiscoveryEnabled);
        await manager.UnbindAsync();
        Assert.Null(manager.Current.Identity);
        Assert.False(manager.Current.AutoDiscoveryEnabled);
        Assert.Equal(GameSessionState.Unbound, manager.Current.State);
    }
    [Fact] public async Task Insufficient_identity_and_ambiguous_profiles_do_not_become_ready()
    {
        var candidate = Candidate("b");
        await using var manager = new GameSessionManager(new FakeCatalog(), new ImmediateProvider(candidate, IdentityVerification.Insufficient),
            new FakeProfiles([Profile, Profile with { Id = "other" }]), NullLogger<GameSessionManager>.Instance);
        await manager.InitializeAsync(new(PreferredProfileId: null));
        await manager.BindAsync(candidate);
        Assert.Equal(GameSessionState.BoundUnavailable, manager.Current.State);
        Assert.Equal(ProfileValidationKind.AmbiguousProfile, manager.Current.ProfileValidation?.Kind);
    }
    [Fact] public async Task Ambiguous_profile_selection_keeps_binding_and_can_recover()
    {
        var candidate = Candidate("profile");
        await using var manager = new GameSessionManager(new FakeCatalog(), new ImmediateProvider(candidate),
            new FakeProfiles([Profile, Profile with { Id = "other" }]), NullLogger<GameSessionManager>.Instance);
        await manager.InitializeAsync(new(PreferredProfileId: null));
        await manager.BindAsync(candidate);
        var session = manager.Current.SessionId;
        Assert.Equal(GameSessionState.NeedsSelection, manager.Current.State);
        Assert.Equal(SelectionKind.Profile, manager.Current.SelectionKind);
        await manager.SelectProfileAsync("other");
        Assert.Equal(GameSessionState.Ready, manager.Current.State);
        Assert.Equal(session, manager.Current.SessionId);
        Assert.Equal("other", manager.Current.ProfileValidation?.ProfileId);
    }
    [Fact] public async Task Reused_window_identity_invalidates_the_old_session()
    {
        var candidate = Candidate("reused");
        var time = new ManualTimeProvider();
        await using var manager = new GameSessionManager(new FakeCatalog(),
            new SequenceProvider(Read(candidate), Read(candidate with { ProcessStartedAt = DateTimeOffset.UnixEpoch.AddMinutes(1) })),
            new FakeProfiles(), NullLogger<GameSessionManager>.Instance, time);
        await manager.InitializeAsync(new());
        await manager.BindAsync(candidate);
        var session = manager.Current.SessionId;
        if (manager.Current.Identity is not null)
        {
            await time.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5));
            time.Advance(TimeSpan.FromMilliseconds(200));
        }
        await WaitForState(manager, GameSessionState.Unbound);
        Assert.Null(manager.Current.SessionId);
        Assert.NotEqual(session, manager.Current.SessionId);
        Assert.Equal(2, manager.Current.BindingGeneration);
    }
    [Fact] public async Task Identity_information_can_recover_on_the_next_sample()
    {
        var candidate = Candidate("recover");
        var time = new ManualTimeProvider();
        await using var manager = new GameSessionManager(new FakeCatalog(),
            new SequenceProvider(Read(candidate, IdentityVerification.Insufficient), Read(candidate)),
            new FakeProfiles(), NullLogger<GameSessionManager>.Instance, time);
        await manager.InitializeAsync(new());
        await manager.BindAsync(candidate);
        Assert.Equal(GameSessionState.BoundUnavailable, manager.Current.State);
        await time.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromMilliseconds(200));
        await WaitForState(manager, GameSessionState.Ready);
        Assert.Equal(IdentityVerification.Verified, manager.Current.Identity?.Verification);
    }
    private sealed class SequenceProvider(WindowReadResult first, WindowReadResult second) : IWindowGeometryProvider
    {
        private int _reads;
        public Task<WindowReadResult> ReadAsync(WindowIdentity identity, CancellationToken token) =>
            Task.FromResult(Interlocked.Increment(ref _reads) == 1 ? first : second);
    }
    [Fact] public async Task Slow_tracking_read_never_overlaps_and_closes_cleanly()
    {
        var candidate = Candidate("serial");
        var provider = new SlowProvider(candidate);
        var time = new ManualTimeProvider();
        var manager = new GameSessionManager(new FakeCatalog(), provider, new FakeProfiles(),
            NullLogger<GameSessionManager>.Instance, time);
        await manager.InitializeAsync(new());
        Assert.Null(await manager.BindAsync(candidate));
        if (!provider.TrackingStarted.Task.IsCompleted)
        {
            var timerCreated = time.TimerCreated;
            if (await Task.WhenAny(provider.TrackingStarted.Task, timerCreated).WaitAsync(TimeSpan.FromSeconds(5)) == timerCreated)
                time.Advance(TimeSpan.FromMilliseconds(200));
        }
        await provider.TrackingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (var i = 0; i < 20; i++) time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, provider.ReadCount); // binding read + one in-flight tracking read
        provider.CompleteTracking(Read(candidate, size: new(1280, 720)));
        await WaitForState(manager, GameSessionState.UnsupportedResolution);
        await manager.DisposeAsync();
        time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(2, provider.ReadCount);
    }
    private static async Task WaitForState(GameSessionManager manager, GameSessionState state)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnUpdate(GameSessionSnapshot snapshot) { if (snapshot.State == state) signal.TrySetResult(); }
        manager.Updated += OnUpdate;
        if (manager.Current.State == state) signal.TrySetResult();
        try { await signal.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { manager.Updated -= OnUpdate; }
    }
    private sealed class SlowProvider(WindowCandidate candidate) : IWindowGeometryProvider
    {
        private readonly TaskCompletionSource<WindowReadResult> _tracking = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource TrackingStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ReadCount { get; private set; }
        public Task<WindowReadResult> ReadAsync(WindowIdentity identity, CancellationToken token)
        {
            ReadCount++;
            if (ReadCount == 1) return Task.FromResult(Read(candidate));
            TrackingStarted.TrySetResult();
            return _tracking.Task;
        }
        public void CompleteTracking(WindowReadResult result) => _tracking.SetResult(result);
    }
    private sealed class FakeCatalog : IWindowCatalog
    {
        public Task<WindowCatalogResult> EnumerateAsync(CancellationToken token) => Task.FromResult(new WindowCatalogResult([]));
    }
    private sealed class FakeProfiles(IReadOnlyList<GameWindowProfile>? profiles = null) : IProfileRepository
    {
        public Task<ProfileLoadResult> LoadAsync(CancellationToken token) => Task.FromResult(new ProfileLoadResult(profiles ?? [Profile]));
    }
    private sealed class ImmediateProvider(WindowCandidate candidate, IdentityVerification status = IdentityVerification.Verified) : IWindowGeometryProvider
    {
        public Task<WindowReadResult> ReadAsync(WindowIdentity identity, CancellationToken token) => Task.FromResult(Read(candidate, status));
    }
    private sealed class ControlledProvider : IWindowGeometryProvider
    {
        private readonly TaskCompletionSource<WindowReadResult> _a = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ARequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<WindowReadResult> ReadAsync(WindowIdentity identity, CancellationToken token)
        {
            if (identity.Id.Value == "a") { ARequested.TrySetResult(); return _a.Task; }
            return Task.FromResult(Read(Candidate("b", 2)));
        }
        public void CompleteA(WindowReadResult result) => _a.SetResult(result);
    }
}
