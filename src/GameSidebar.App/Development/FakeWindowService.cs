using GameSidebar.Application.Abstractions;
using GameSidebar.Core.Geometry;
using GameSidebar.Core.Sessions;

namespace GameSidebar.App.Development;

public enum DemoScenario { NoWindow, SingleWindow, MultipleWindows, DelayedARead, Moved, UnsupportedSize, Minimized, Restored, Disappeared, Restarted, InsufficientIdentity }
public sealed class FakeWindowService : IWindowCatalog, IWindowGeometryProvider
{
    private readonly object _gate = new();
    private DemoScenario _scenario = DemoScenario.SingleWindow;
    private int _generation;
    private int _aReads;
    private TaskCompletionSource _releaseDelay = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public DemoScenario Scenario { get { lock (_gate) return _scenario; } }
    public void SetScenario(DemoScenario scenario)
    {
        lock (_gate)
        {
            if (scenario == DemoScenario.Restarted) _generation++;
            _releaseDelay.TrySetResult();
            _scenario = scenario;
            _aReads = 0;
            _releaseDelay = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
    public void ReleaseDelayedRead() { lock (_gate) _releaseDelay.TrySetResult(); }
    public Task<WindowCatalogResult> EnumerateAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_scenario is DemoScenario.NoWindow or DemoScenario.Disappeared) return Task.FromResult(new WindowCatalogResult([]));
            var count = _scenario is DemoScenario.MultipleWindows or DemoScenario.DelayedARead ? 2 : 1;
            var list = Enumerable.Range(0, count).Select(i => Candidate(i, _generation)).ToArray();
            return Task.FromResult(new WindowCatalogResult(list));
        }
    }
    private static WindowCandidate Candidate(int index, int generation) => new(new($"demo:{generation}:{index}"), 5000 + index,
        "GameSidebar.TestWindow.exe", $"Demo 测试窗口 {index + 1}", "FakeWindow", DateTimeOffset.UnixEpoch.AddMinutes(generation),
        true, false, false, false, false, false, null);
    public async Task<WindowReadResult> ReadAsync(WindowIdentity identity, CancellationToken cancellationToken)
    {
        DemoScenario scenario;
        int generation;
        lock (_gate) { scenario = _scenario; generation = _generation; }
        Task? delayed = null;
        if (scenario == DemoScenario.DelayedARead && identity.Id.Value == $"demo:{generation}:0")
        {
            lock (_gate) { if (++_aReads > 1) delayed = _releaseDelay.Task; }
            if (delayed is not null) await delayed; // Deliberately ignores cancellation to test generation filtering.
        }
        if (scenario == DemoScenario.Restarted && identity.Id.Value != $"demo:{generation}:0")
            return new(identity with { Verification = IdentityVerification.Mismatch }, null, false, false,
                new(WindowErrorCode.WindowDestroyed, "Demo 进程重启，旧身份失效"));
        if (scenario is DemoScenario.Disappeared or DemoScenario.NoWindow)
            return new(identity with { Verification = IdentityVerification.Destroyed }, null, false, false,
                new(WindowErrorCode.WindowDestroyed, "Demo 窗口已消失"));
        if (scenario == DemoScenario.InsufficientIdentity)
            return new(identity with { Verification = IdentityVerification.Insufficient }, null, false, false,
                new(WindowErrorCode.IdentityInsufficient, "Demo 进程创建时间暂不可读"));
        if (scenario == DemoScenario.Minimized)
            return new(identity, null, false, true, new(WindowErrorCode.ApiFailure, "Demo 窗口已最小化"));
        var x = scenario == DemoScenario.Moved ? -300 : 100;
        var width = scenario == DemoScenario.UnsupportedSize ? 1280 : 1920;
        var height = scenario == DemoScenario.UnsupportedSize ? 720 : 1080;
        var geometry = new WindowGeometrySnapshot(new(x, 80, x + width + 16, 80 + height + 39),
            new(x + 8, 88, x + width + 8, height + 111), VisibleFrameSource.Dwm,
            new(width, height), new(x + 8, 111), new(x + 8, 111, x + 8 + width, 111 + height),
            new(-1920, 0, 1920, 1080), new(-1920, 0, 1920, 1040), "DemoMonitor", 120,
            DpiAwarenessKind.PerMonitorAwareV2, DpiAwarenessKind.PerMonitorAwareV2,
            GeometryValidity.Valid, DateTimeOffset.UtcNow);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return new(identity with { Verification = IdentityVerification.Verified }, geometry, true, false);
    }
}
