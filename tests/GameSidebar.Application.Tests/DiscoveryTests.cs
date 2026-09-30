using GameSidebar.Application.Abstractions;
using GameSidebar.Application.Discovery;
using GameSidebar.Application.Sessions;
using GameSidebar.Core.Geometry;
using GameSidebar.Core.Profiles;
using GameSidebar.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameSidebar.Application.Tests;

public sealed class DiscoveryTests
{
    private static readonly GameWindowProfile Profile = new(1, "diagnostic", ProfilePurpose.DiagnosticOnly,
        null, new(1920, 1080), new(360, 48));

    [Fact]
    public void Match_filters_ineligible_windows_and_matches_exe_case_insensitively()
    {
        var regular = Candidate("regular");
        var minimized = Candidate("minimized") with { IsMinimized = true };
        var candidates = new[]
        {
            regular, minimized,
            Candidate("owned") with { IsOwned = true },
            Candidate("hidden") with { IsVisible = false },
            Candidate("tool") with { IsToolWindow = true },
            Candidate("cloaked") with { IsCloaked = true },
            Candidate("self") with { IsSelf = true },
            Candidate("denied") with { Error = new(WindowErrorCode.PermissionDenied, "进程受限") }
        };
        var result = WindowMatchService.Match(new(Exe: "TEST.EXE"), candidates);
        Assert.Null(result.Error);
        Assert.Equal([regular.Id, minimized.Id], result.Candidates.Select(c => c.Id));
        Assert.True(candidates[2].IsSelectable); // Owned windows remain manually selectable.
        Assert.False(candidates[3].IsSelectable);
    }

    [Fact]
    public void Malformed_pattern_is_configuration_error_not_no_candidate()
    {
        var result = WindowMatchService.Match(new(Exe: "test.exe", TitleRule: "["), [Candidate("a")]);
        Assert.Equal(WindowErrorCode.InvalidConfiguration, result.Error?.Code);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Complete_path_never_matches_a_same_named_executable_elsewhere()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GameSidebarTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var wanted = Path.Combine(directory, "game.exe");
        File.WriteAllText(wanted, "fixture");
        try
        {
            var sameName = Candidate("other") with { Executable = "game.exe",
                ExecutablePath = Path.Combine(directory, "other", "game.exe") };
            var correct = Candidate("correct") with { Executable = "game.exe", ExecutablePath = wanted };
            var result = WindowMatchService.Match(new(ExecutablePath: wanted), [sameName, correct]);
            Assert.Equal([correct.Id], result.Candidates.Select(x => x.Id));
            var unreadable = Candidate("unknown") with { Executable = "game.exe", ExecutablePath = null };
            Assert.Equal(WindowErrorCode.PermissionDenied,
                WindowMatchService.Match(new(ExecutablePath: wanted), [unreadable]).Error?.Code);
            Assert.Equal(WindowErrorCode.InvalidConfiguration,
                WindowMatchService.Match(new(ExecutablePath: Path.Combine(directory, "missing.exe")), [sameName]).Error?.Code);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Regex_timeout_is_a_distinct_configuration_failure()
    {
        var candidate = Candidate("slow") with { Title = new string('a', 30_000) + "!" };
        var result = WindowMatchService.Match(new(Exe: "test.exe", TitleRule: "^(a+)+$"), [candidate]);
        Assert.Equal(WindowErrorCode.RegexTimeout, result.Error?.Code);
        Assert.Empty(result.Candidates);
    }

    [Theory]
    [InlineData(0, GameSessionState.Searching, SelectionKind.None)]
    [InlineData(1, GameSessionState.Ready, SelectionKind.None)]
    [InlineData(2, GameSessionState.NeedsSelection, SelectionKind.Window)]
    public async Task Automatic_discovery_handles_zero_one_or_many_candidates(int count,
        GameSessionState expected, SelectionKind selection)
    {
        var candidates = Enumerable.Range(0, count).Select(i => Candidate($"window-{i}")).ToArray();
        var catalog = new Catalog(candidates);
        await using var manager = new GameSessionManager(catalog, new Provider(), new Profiles(),
            NullLogger<GameSessionManager>.Instance);
        await manager.InitializeAsync(new(Exe: "test.exe"));
        await catalog.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (count == 0) await WaitForSnapshot(manager, value => value.Error?.Code == WindowErrorCode.NoCandidate);
        else await WaitForState(manager, expected);
        Assert.Equal(expected, manager.Current.State);
        Assert.Equal(selection, manager.Current.SelectionKind);
        if (count == 0) Assert.Equal(WindowErrorCode.NoCandidate, manager.Current.Error?.Code);
        if (count == 1) Assert.Equal(BindingMode.Auto, manager.Current.BindingMode);
        if (count == 2) Assert.Null(manager.Current.Identity);
    }

    [Fact]
    public async Task Catalog_error_is_not_treated_as_empty_search_result()
    {
        var catalog = new Catalog([], new(WindowErrorCode.PermissionDenied, "列窗权限失败"));
        await using var manager = new GameSessionManager(catalog, new Provider(), new Profiles(),
            NullLogger<GameSessionManager>.Instance);
        await manager.InitializeAsync(new(Exe: "test.exe"));
        await WaitForState(manager, GameSessionState.Faulted);
        Assert.Equal(WindowErrorCode.PermissionDenied, manager.Current.Error?.Code);
    }

    private static WindowCandidate Candidate(string id) => new(new(id), 2200, "test.exe", id, "Test",
        DateTimeOffset.UnixEpoch, true, false, false, false, false, false, null);

    private static Task WaitForState(GameSessionManager manager, GameSessionState state) =>
        WaitForSnapshot(manager, value => value.State == state);

    private static async Task WaitForSnapshot(GameSessionManager manager, Func<GameSessionSnapshot, bool> matches)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnUpdate(GameSessionSnapshot value) { if (matches(value)) signal.TrySetResult(); }
        manager.Updated += OnUpdate;
        if (matches(manager.Current)) signal.TrySetResult();
        try { await signal.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { manager.Updated -= OnUpdate; }
    }

    private sealed class Catalog(IReadOnlyList<WindowCandidate> candidates, WindowOperationError? error = null) : IWindowCatalog
    {
        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<WindowCatalogResult> EnumerateAsync(CancellationToken token)
        {
            Called.TrySetResult();
            return Task.FromResult(new WindowCatalogResult(candidates, error));
        }
    }
    private sealed class Profiles : IProfileRepository
    {
        public Task<ProfileLoadResult> LoadAsync(CancellationToken token) =>
            Task.FromResult(new ProfileLoadResult([Profile]));
    }
    private sealed class Provider : IWindowGeometryProvider
    {
        public Task<WindowReadResult> ReadAsync(WindowIdentity identity, CancellationToken token)
        {
            var geometry = new WindowGeometrySnapshot(new(0, 0, 1936, 1119), new(8, 8, 1928, 1111),
                VisibleFrameSource.Dwm, new(1920, 1080), new(8, 31), new(8, 31, 1928, 1111),
                new(0, 0, 2560, 1440), new(0, 0, 2560, 1400), "monitor", 96,
                DpiAwarenessKind.PerMonitorAwareV2, DpiAwarenessKind.PerMonitorAwareV2,
                GeometryValidity.Valid, DateTimeOffset.UtcNow);
            return Task.FromResult(new WindowReadResult(identity with { Verification = IdentityVerification.Verified },
                geometry, true, false));
        }
    }
}
