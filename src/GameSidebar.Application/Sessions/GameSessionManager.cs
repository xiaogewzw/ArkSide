using GameSidebar.Application.Abstractions;
using GameSidebar.Application.Discovery;
using GameSidebar.Core.Geometry;
using GameSidebar.Core.Profiles;
using GameSidebar.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace GameSidebar.Application.Sessions;

public sealed class GameSessionManager : IAsyncDisposable
{
    private readonly IWindowCatalog _catalog;
    private readonly IWindowGeometryProvider _provider;
    private readonly IProfileRepository _profiles;
    private readonly ILogger<GameSessionManager> _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private WindowTracker? _tracker;
    private CancellationTokenSource? _bindingCancellation;
    private TargetWindowRule _rule = new();
    private IReadOnlyList<GameWindowProfile> _loadedProfiles = [];
    private GameSessionSnapshot _snapshot;
    private bool _disposed;

    public GameSessionManager(IWindowCatalog catalog, IWindowGeometryProvider provider, IProfileRepository profiles,
        ILogger<GameSessionManager> logger, TimeProvider? time = null)
    {
        _catalog = catalog; _provider = provider; _profiles = profiles; _logger = logger;
        _time = time ?? TimeProvider.System;
        _snapshot = GameSessionSnapshot.Initial(_time.GetUtcNow());
    }

    public GameSessionSnapshot Current => Volatile.Read(ref _snapshot);
    public event Action<GameSessionSnapshot>? Updated;

    public async Task InitializeAsync(TargetWindowRule rule, CancellationToken cancellationToken = default)
    {
        var profiles = await _profiles.LoadAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _rule = rule;
            _loadedProfiles = profiles.Profiles;
            var configError = profiles.Error ?? ProfileRules.ValidateProfiles(profiles.Profiles) ?? ProfileRules.Validate(rule);
            Publish(Current with { State = configError is not null ? GameSessionState.Faulted : rule.IsConfigured ? GameSessionState.Searching : GameSessionState.NotConfigured,
                AutoDiscoveryEnabled = configError is null && rule.IsConfigured,
                Error = configError ?? (rule.IsConfigured ? null : new WindowOperationError(WindowErrorCode.NoConfiguration, "未配置目标，可手动选择窗口")),
                ObservedAt = _time.GetUtcNow() });
            _tracker ??= new WindowTracker(TrackOnceAsync, _time);
            _tracker.Start(_shutdown.Token);
        }
        finally { _gate.Release(); }
    }

    public async Task SetRuleAsync(TargetWindowRule rule, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _rule = rule;
            var error = ProfileRules.Validate(rule);
            var next = Current with { Error = error, AutoDiscoveryEnabled = false, ObservedAt = _time.GetUtcNow() };
            if (error is not null) Publish(next with { State = GameSessionState.Faulted });
            else if (next.Identity is not null) Revalidate(next);
            else Publish(next with { State = GameSessionState.NotConfigured });
        }
        finally { _gate.Release(); }
    }

    public async Task ReloadProfilesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _profiles.LoadAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _loadedProfiles = result.Profiles;
            var error = result.Error ?? ProfileRules.ValidateProfiles(result.Profiles);
            if (error is not null) Publish(Current with { State = GameSessionState.Faulted, Error = error });
            else if (Current.Identity is not null) Revalidate(Current);
            else Publish(Current with { State = _rule.IsConfigured && Current.AutoDiscoveryEnabled ? GameSessionState.Searching : GameSessionState.NotConfigured, Error = null });
        }
        finally { _gate.Release(); }
    }

    public async Task StartDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var error = ProfileRules.Validate(_rule) ?? ProfileRules.ValidateProfiles(_loadedProfiles);
            if (error is not null) Publish(Current with { State = GameSessionState.Faulted, Error = error });
            else if (!_rule.IsConfigured) Publish(Current with { State = GameSessionState.NotConfigured,
                AutoDiscoveryEnabled = false, Error = new(WindowErrorCode.NoConfiguration, "未配置目标，可手动选择窗口") });
            else Publish(Current with { AutoDiscoveryEnabled = true,
                State = Current.Identity is null ? GameSessionState.Searching : Current.State, Error = null });
        }
        finally { _gate.Release(); }
    }

    public async Task StopDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Current.Identity is null) _bindingCancellation?.Cancel();
            Publish(Current with { AutoDiscoveryEnabled = false,
                BindingGeneration = Current.Identity is null ? Current.BindingGeneration + 1 : Current.BindingGeneration,
                State = Current.Identity is null ? GameSessionState.Unbound : Current.State,
                SelectionKind = Current.Identity is null ? SelectionKind.None : Current.SelectionKind });
        }
        finally { _gate.Release(); }
    }

    public async Task<WindowOperationError?> BindAsync(WindowCandidate candidate, BindingMode mode = BindingMode.Manual,
        CancellationToken cancellationToken = default, long? expectedGeneration = null)
    {
        if (!candidate.IsSelectable) return new(WindowErrorCode.WindowDestroyed, candidate.ExclusionReason ?? "不可绑定窗口");
        long generation;
        CancellationToken bindingToken;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (mode == BindingMode.Auto && (!Current.AutoDiscoveryEnabled || Current.Identity is not null ||
                (expectedGeneration is not null && Current.BindingGeneration != expectedGeneration)))
                return new(WindowErrorCode.Cancelled, "自动搜索结果已过期");
            _bindingCancellation?.Cancel(); _bindingCancellation?.Dispose();
            _bindingCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, cancellationToken);
            bindingToken = _bindingCancellation.Token;
            generation = Current.BindingGeneration + 1;
            Publish(Current with { SessionId = null, BindingGeneration = generation, GeometryVersion = 0,
                Identity = null, Geometry = null, ProfileValidation = null, State = GameSessionState.Binding,
                SelectionKind = SelectionKind.None, BindingMode = mode, Error = null,
                ObservedAt = _time.GetUtcNow() });
        }
        finally { _gate.Release(); }

        WindowReadResult read;
        var initial = new WindowIdentity(candidate.Id, candidate.ProcessId, candidate.ProcessStartedAt,
            candidate.ProcessStartedAt is null ? IdentityVerification.Insufficient : IdentityVerification.Verified);
        try { read = await _provider.ReadAsync(initial, bindingToken); }
        catch (OperationCanceledException) when (bindingToken.IsCancellationRequested) { return new(WindowErrorCode.Cancelled, "绑定已取消"); }
        catch (Exception e) { return await FailBindingAsync(generation, initial, new(WindowErrorCode.ApiFailure, e.Message)); }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Current.BindingGeneration != generation) return new(WindowErrorCode.Cancelled, "绑定已被新操作替代");
            if (!SameIdentity(initial, read.Identity) || read.Identity.Verification is IdentityVerification.Destroyed or IdentityVerification.Mismatch)
            {
                var error = read.Error ?? new WindowOperationError(WindowErrorCode.WindowDestroyed, "候选窗口已失效或身份变化");
                Publish(Current with { State = GameSessionState.GameLost, Error = error });
                return error;
            }
            var bound = Current with { SessionId = Guid.NewGuid(), Identity = read.Identity, Geometry = read.Geometry,
                GeometryVersion = read.Geometry?.IsUsable == true ? 1 : 0,
                IsForeground = read.IsForeground, IsMinimized = read.IsMinimized,
                State = GameSessionState.Validating, ObservedAt = _time.GetUtcNow(), Error = read.Error };
            Publish(bound);
            Revalidate(bound);
            return null;
        }
        finally { _gate.Release(); }
    }

    private async Task<WindowOperationError> FailBindingAsync(long generation, WindowIdentity identity, WindowOperationError error)
    {
        await _gate.WaitAsync();
        try
        {
            if (Current.BindingGeneration == generation)
                Publish(Current with { SessionId = Guid.NewGuid(), Identity = identity, Geometry = null,
                    State = GameSessionState.BoundUnavailable, Error = error });
        }
        finally { _gate.Release(); }
        return error;
    }

    public async Task UnbindAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _bindingCancellation?.Cancel();
            Publish(Current with { SessionId = null, BindingGeneration = Current.BindingGeneration + 1,
                GeometryVersion = 0, Identity = null, Geometry = null, ProfileValidation = null,
                State = GameSessionState.Unbound, SelectionKind = SelectionKind.None, BindingMode = BindingMode.None,
                AutoDiscoveryEnabled = false, Error = null, ObservedAt = _time.GetUtcNow() });
        }
        finally { _gate.Release(); }
    }

    public async Task SelectProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { _rule = _rule with { PreferredProfileId = profileId }; Revalidate(Current); }
        finally { _gate.Release(); }
    }

    public async Task<WindowCatalogResult> GetCandidatesAsync(CancellationToken cancellationToken = default) =>
        await _catalog.EnumerateAsync(cancellationToken);

    private async Task<TimeSpan> TrackOnceAsync(CancellationToken token)
    {
        try
        {
            var before = Current;
            if (before.Identity is not null)
            {
                WindowReadResult read;
                try { read = await _provider.ReadAsync(before.Identity, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception e)
                {
                    read = new(before.Identity with { Verification = IdentityVerification.Insufficient }, null,
                        before.IsForeground, before.IsMinimized, new(WindowErrorCode.ApiFailure, e.Message));
                }
                await ApplyReadAsync(before.BindingGeneration, read, token);
                return TimeSpan.FromMilliseconds(200);
            }
            if (before.AutoDiscoveryEnabled && before.State != GameSessionState.Faulted)
            {
                var catalog = await _catalog.EnumerateAsync(token);
                await ApplyDiscoveryAsync(before.BindingGeneration, catalog, token);
                return TimeSpan.FromSeconds(1);
            }
            return TimeSpan.FromMilliseconds(200);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            _logger.LogError(e, "Tracker failure");
            await _gate.WaitAsync(CancellationToken.None);
            try { Publish(Current with { State = GameSessionState.Faulted, Error = new(WindowErrorCode.ApiFailure, e.Message) }); }
            finally { _gate.Release(); }
            return TimeSpan.FromSeconds(1);
        }
    }

    private async Task ApplyDiscoveryAsync(long generation, WindowCatalogResult catalog, CancellationToken token)
    {
        WindowCandidate? single = null;
        await _gate.WaitAsync(token);
        try
        {
            if (Current.BindingGeneration != generation || Current.Identity is not null || !Current.AutoDiscoveryEnabled) return;
            if (catalog.Error is not null) { Publish(Current with { State = GameSessionState.Faulted, Error = catalog.Error }); return; }
            var match = WindowMatchService.Match(_rule, catalog.Candidates);
            if (match.Error is not null) { Publish(Current with { State = GameSessionState.Faulted, Error = match.Error }); return; }
            if (match.Candidates.Count > 1) Publish(Current with { State = GameSessionState.NeedsSelection, SelectionKind = SelectionKind.Window,
                Error = null, ObservedAt = _time.GetUtcNow() });
            else if (match.Candidates.Count == 0) Publish(Current with { State = GameSessionState.Searching, SelectionKind = SelectionKind.None,
                Error = new(WindowErrorCode.NoCandidate, "未找到匹配窗口"), ObservedAt = _time.GetUtcNow() });
            else single = match.Candidates[0];
        }
        finally { _gate.Release(); }
        if (single is not null) await BindAsync(single, BindingMode.Auto, token, generation);
    }

    private async Task ApplyReadAsync(long generation, WindowReadResult read, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var current = Current;
            if (current.BindingGeneration != generation || current.Identity is null) return;
            if (!SameIdentity(current.Identity, read.Identity) || read.Identity.Verification is IdentityVerification.Mismatch or IdentityVerification.Destroyed)
            {
                _bindingCancellation?.Cancel();
                var lost = current with { BindingGeneration = generation + 1, Geometry = current.Geometry is null ? null : current.Geometry with { Validity = GeometryValidity.Stale },
                    State = GameSessionState.GameLost, Error = read.Error ?? new(WindowErrorCode.WindowDestroyed, "窗口身份失效"), ObservedAt = _time.GetUtcNow() };
                Publish(lost);
                Publish(lost with { SessionId = null, Identity = null, Geometry = null, ProfileValidation = null,
                    State = current.BindingMode == BindingMode.Auto && current.AutoDiscoveryEnabled && _rule.IsConfigured ? GameSessionState.Searching : GameSessionState.Unbound,
                    SelectionKind = SelectionKind.None, BindingMode = BindingMode.None });
                return;
            }
            var geometry = read.Geometry ?? (current.Geometry is null ? null : current.Geometry with { Validity = GeometryValidity.Stale });
            var nextVersion = geometry is not null && current.Geometry is not null && !geometry.SameMapping(current.Geometry) ? current.GeometryVersion + 1 : current.GeometryVersion;
            var next = current with { Identity = read.Identity, Geometry = geometry, GeometryVersion = nextVersion,
                IsForeground = read.IsForeground, IsMinimized = read.IsMinimized, Error = read.Error,
                ObservedAt = _time.GetUtcNow() };
            Revalidate(next);
        }
        finally { _gate.Release(); }
    }

    private static bool SameIdentity(WindowIdentity expected, WindowIdentity observed) =>
        expected.Id == observed.Id && expected.ProcessId == observed.ProcessId &&
        (expected.ProcessStartedAt is null || expected.ProcessStartedAt == observed.ProcessStartedAt);

    private void Revalidate(GameSessionSnapshot source)
    {
        if (source.Identity is null) return;
        var configurationError = ProfileRules.Validate(_rule) ?? ProfileRules.ValidateProfiles(_loadedProfiles);
        if (configurationError is not null)
        {
            Publish(source with { State = GameSessionState.Faulted, Error = configurationError });
            return;
        }
        var profile = ProfileRules.Match(_loadedProfiles, source.Geometry, _rule.PreferredProfileId);
        var state = source.Identity.Verification != IdentityVerification.Verified || source.IsMinimized ||
            source.Geometry?.IsUsable != true || source.Error is not null ? GameSessionState.BoundUnavailable :
            profile.Kind switch
            {
                ProfileValidationKind.Matched => GameSessionState.Ready,
                ProfileValidationKind.UnsupportedResolution => GameSessionState.UnsupportedResolution,
                ProfileValidationKind.AmbiguousProfile => GameSessionState.NeedsSelection,
                ProfileValidationKind.InvalidProfile => GameSessionState.Faulted,
                _ => GameSessionState.BoundUnavailable
            };
        Publish(source with { ProfileValidation = profile, State = state,
            SelectionKind = state == GameSessionState.NeedsSelection ? SelectionKind.Profile : SelectionKind.None,
            Error = state == GameSessionState.Faulted ? new(WindowErrorCode.InvalidConfiguration, profile.Reason) : source.Error });
    }

    private void Publish(GameSessionSnapshot snapshot)
    {
        var prior = Current;
        Volatile.Write(ref _snapshot, snapshot);
        if (prior.State != snapshot.State || prior.BindingGeneration != snapshot.BindingGeneration)
            _logger.LogInformation("Session {SessionId} generation {Generation}: {From} -> {To}; error {Error}",
                snapshot.SessionId, snapshot.BindingGeneration, prior.State, snapshot.State, snapshot.Error?.Code);
        var handlers = Updated;
        if (handlers is null) return;
        foreach (Action<GameSessionSnapshot> handler in handlers.GetInvocationList())
            try { handler(snapshot); } catch (Exception e) { _logger.LogWarning(e, "Snapshot subscriber failed"); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _gate.WaitAsync();
        try
        {
            Publish(Current with { BindingGeneration = Current.BindingGeneration + 1, AutoDiscoveryEnabled = false,
                Identity = null, State = GameSessionState.Unbound });
            _bindingCancellation?.Cancel(); _shutdown.Cancel();
        }
        finally { _gate.Release(); }
        if (_tracker is not null) await _tracker.Completion;
        _bindingCancellation?.Dispose(); _shutdown.Dispose(); _gate.Dispose();
    }
}
