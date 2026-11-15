using GameSidebar.App.Development;
using GameSidebar.Application.Abstractions;
using GameSidebar.Application.Sessions;
using GameSidebar.Core.Profiles;
using GameSidebar.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameSidebar.Application.Tests;

public sealed class StandaloneSessionTests
{
    private static readonly TargetWindowRule Rule = new(Exe: "GameSidebar.TestWindow.exe");

    [Fact]
    public async Task Startup_automatically_binds_an_already_running_game()
    {
        var windows = new FakeWindowService();
        await using var manager = Create(windows, new ManualTimeProvider());
        await manager.InitializeAsync(Rule);
        await WaitFor(manager, s => s.CanPreview);
        Assert.Equal(BindingMode.Auto, manager.Current.BindingMode);
        Assert.NotNull(manager.Current.Geometry);
    }

    [Fact]
    public async Task Retry_without_game_stays_unbound_then_binds_when_game_appears()
    {
        var windows = new FakeWindowService();
        windows.SetScenario(DemoScenario.NoWindow);
        var time = new ManualTimeProvider();
        await using var manager = Create(windows, time);
        await manager.InitializeAsync(Rule);
        await time.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5));
        await manager.SearchNowAsync();
        Assert.Null(manager.Current.Identity);
        Assert.Equal(WindowErrorCode.NoCandidate, manager.Current.Error?.Code);
        await manager.SearchNowAsync();
        Assert.Equal(WindowErrorCode.NoCandidate, manager.Current.Error?.Code);
        windows.SetScenario(DemoScenario.SingleWindow);
        // No clock advance: the button must perform a fresh search immediately.
        await manager.SearchNowAsync();
        Assert.True(manager.Current.CanPreview);
        Assert.Equal(BindingMode.Auto, manager.Current.BindingMode);
    }

    [Fact]
    public async Task Game_exit_clears_binding_and_retry_creates_a_new_session()
    {
        var windows = new FakeWindowService();
        var time = new ManualTimeProvider();
        await using var manager = Create(windows, time);
        await manager.InitializeAsync(Rule);
        await WaitFor(manager, s => s.CanPreview);
        var previousSession = manager.Current.SessionId;
        windows.SetScenario(DemoScenario.Disappeared);
        await time.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromSeconds(1));
        await WaitFor(manager, s => s.Identity is null);
        Assert.Null(manager.Current.SessionId);
        Assert.False(manager.Current.CanPreview);
        Assert.True(manager.Current.AutoDiscoveryEnabled);
        await manager.SearchNowAsync();
        Assert.Equal(WindowErrorCode.NoCandidate, manager.Current.Error?.Code);
        windows.SetScenario(DemoScenario.Restarted);
        await manager.SearchNowAsync();
        Assert.True(manager.Current.CanPreview);
        Assert.NotEqual(previousSession, manager.Current.SessionId);
    }

    [Fact]
    public async Task Retry_with_multiple_games_requires_selection()
    {
        var windows = new FakeWindowService();
        windows.SetScenario(DemoScenario.MultipleWindows);
        var time = new ManualTimeProvider();
        await using var manager = Create(windows, time);
        await manager.InitializeAsync(Rule);
        await time.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5));
        await manager.SearchNowAsync();
        Assert.Equal(GameSessionState.NeedsSelection, manager.Current.State);
        Assert.Equal(SelectionKind.Window, manager.Current.SelectionKind);
        Assert.Null(manager.Current.Identity);
    }

    private static GameSessionManager Create(FakeWindowService windows, TimeProvider time) =>
        new(windows, windows, new Profiles(), NullLogger<GameSessionManager>.Instance, time);

    private static async Task WaitFor(GameSessionManager manager, Func<GameSessionSnapshot, bool> predicate)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Updated(GameSessionSnapshot snapshot) { if (predicate(snapshot)) signal.TrySetResult(); }
        manager.Updated += Updated;
        if (predicate(manager.Current)) signal.TrySetResult();
        try { await signal.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { manager.Updated -= Updated; }
    }

    private sealed class Profiles : IProfileRepository
    {
        public Task<ProfileLoadResult> LoadAsync(CancellationToken token) => Task.FromResult(new ProfileLoadResult(
            [new(1, "diagnostic-1920x1080", ProfilePurpose.DiagnosticOnly, null, new(1920, 1080), new(300, 48))]));
    }
}
