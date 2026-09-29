using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSidebar.App.Development;
using GameSidebar.App.Storage;
using GameSidebar.Application.Sessions;
using GameSidebar.Core.Profiles;
using GameSidebar.Core.Sessions;

namespace GameSidebar.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly GameSessionManager _manager;
    private readonly SettingsStore _settings;
    private readonly FakeWindowService? _fake;
    private AppSettings _currentSettings = new();
    private bool _busy;
    private WindowCandidate? _selectedCandidate;
    private string _statusText = "正在启动";
    private string _diagnostics = "";
    private string _notice = "";
    private string _exe = "";
    private string _titleRule = "";
    private string _classRule = "";
    private string _preferredProfileId = "diagnostic-1920x1080";
    private DemoScenario _selectedScenario = DemoScenario.SingleWindow;

    public MainViewModel(GameSessionManager manager, SettingsStore settings, FakeWindowService? fake, bool demo)
    {
        _manager = manager; _settings = settings; _fake = fake; IsDemo = demo;
        ModeText = demo ? "DEMO 模式 · 模拟窗口，非 Windows API 验证" : "Windows 真实窗口模式";
        VersionText = typeof(MainViewModel).Assembly.GetName().Version?.ToString() ?? "未知";
        _manager.Updated += OnUpdated;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        BindCommand = new AsyncRelayCommand(BindAsync);
        UnbindCommand = new AsyncRelayCommand(UnbindAsync);
        StartDiscoveryCommand = new AsyncRelayCommand(StartDiscoveryAsync);
        StopDiscoveryCommand = new AsyncRelayCommand(StopDiscoveryAsync);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
        SelectProfileCommand = new AsyncRelayCommand(SelectProfileAsync);
        ReloadProfilesCommand = new AsyncRelayCommand(ReloadProfilesAsync);
        ApplyScenarioCommand = new AsyncRelayCommand(ApplyScenarioAsync);
        ReleaseDelayedReadCommand = new RelayCommand(() => _fake?.ReleaseDelayedRead());
    }
    public string ModeText { get; }
    public string VersionText { get; }
    public bool IsDemo { get; }
    public bool IsBusy { get => _busy; private set => SetProperty(ref _busy, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string Diagnostics { get => _diagnostics; private set => SetProperty(ref _diagnostics, value); }
    public string Notice { get => _notice; private set => SetProperty(ref _notice, value); }
    public string Exe { get => _exe; set => SetProperty(ref _exe, value); }
    public string TitleRule { get => _titleRule; set => SetProperty(ref _titleRule, value); }
    public string ClassRule { get => _classRule; set => SetProperty(ref _classRule, value); }
    public string PreferredProfileId { get => _preferredProfileId; set => SetProperty(ref _preferredProfileId, value); }
    public DemoScenario SelectedScenario { get => _selectedScenario; set => SetProperty(ref _selectedScenario, value); }
    public Array Scenarios => Enum.GetValues<DemoScenario>();
    public ObservableCollection<WindowCandidate> Candidates { get; } = [];
    public WindowCandidate? SelectedCandidate { get => _selectedCandidate; set => SetProperty(ref _selectedCandidate, value); }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand BindCommand { get; }
    public IAsyncRelayCommand UnbindCommand { get; }
    public IAsyncRelayCommand StartDiscoveryCommand { get; }
    public IAsyncRelayCommand StopDiscoveryCommand { get; }
    public IAsyncRelayCommand SaveSettingsCommand { get; }
    public IAsyncRelayCommand SelectProfileCommand { get; }
    public IAsyncRelayCommand ReloadProfilesCommand { get; }
    public IAsyncRelayCommand ApplyScenarioCommand { get; }
    public IRelayCommand ReleaseDelayedReadCommand { get; }
    public string DiagnosticJson => JsonSerializer.Serialize(new { version = VersionText, mode = ModeText,
        snapshot = _manager.Current, settings = new { _currentSettings.SchemaVersion } }, JsonStorage.Options);

    public async Task InitializeAsync()
    {
        try { await InitializeCoreAsync(); }
        catch (Exception e) { Notice = $"初始化失败：{e.Message}"; StatusText = "Faulted · 初始化失败，请检查设置和日志"; }
    }
    private async Task InitializeCoreAsync()
    {
        _currentSettings = await _settings.LoadAsync();
        var target = _currentSettings.EffectiveTarget;
        Exe = target.Exe ?? "";
        TitleRule = target.TitleRule ?? "";
        ClassRule = target.ClassRule ?? "";
        PreferredProfileId = target.PreferredProfileId ?? "";
        if (_settings.RecoveryNotice is not null) Notice = _settings.RecoveryNotice;
        await _manager.InitializeAsync(target);
        await RefreshAsync();
    }
    private async Task RunBusyAsync(Func<Task> work)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await work(); }
        catch (Exception e) { Notice = e.Message; }
        finally { IsBusy = false; }
    }
    private Task RefreshAsync() => RunBusyAsync(async () =>
    {
        var result = await _manager.GetCandidatesAsync();
        Candidates.Clear();
        foreach (var candidate in result.Candidates.Where(x => _currentSettings.ShowExcludedWindows || x.IsSelectable))
            Candidates.Add(candidate);
        Notice = result.Error?.Message ?? $"找到 {Candidates.Count} 个窗口；选中后可手动绑定。";
    });
    private Task BindAsync() => RunBusyAsync(async () =>
    {
        if (SelectedCandidate is null) { Notice = "请先选择窗口"; return; }
        var mode = _manager.Current.SelectionKind == SelectionKind.Window && _manager.Current.AutoDiscoveryEnabled ? BindingMode.Auto : BindingMode.Manual;
        var error = await _manager.BindAsync(SelectedCandidate, mode);
        Notice = error?.Message ?? "窗口绑定请求已完成";
    });
    private Task UnbindAsync() => RunBusyAsync(async () => { await _manager.UnbindAsync(); Notice = "已解绑，自动重绑已停用"; });
    private Task StartDiscoveryAsync() => RunBusyAsync(async () => { await _manager.StartDiscoveryAsync(); Notice = _manager.Current.Error?.Message ?? "自动搜索已开始"; });
    private Task StopDiscoveryAsync() => RunBusyAsync(async () => { await _manager.StopDiscoveryAsync(); Notice = "自动搜索已停止，当前绑定仍在跟踪"; });
    private Task SaveSettingsAsync() => RunBusyAsync(async () =>
    {
        var target = _currentSettings.EffectiveTarget with
        {
            Exe = NullIfBlank(Exe), TitleRule = NullIfBlank(TitleRule), ClassRule = NullIfBlank(ClassRule),
            PreferredProfileId = NullIfBlank(PreferredProfileId)
        };
        var error = ProfileRules.Validate(target);
        if (error is not null) { Notice = error.Message; return; }
        _currentSettings = _currentSettings with { Target = target };
        await _settings.SaveAsync(_currentSettings);
        await _manager.SetRuleAsync(target);
        Notice = "设置已保存。可以开始自动搜索；当前目标游戏兼容性仍未验证。";
    });
    private Task SelectProfileAsync() => RunBusyAsync(async () => { await _manager.SelectProfileAsync(PreferredProfileId); Notice = "Profile 偏好已应用；保存设置可跨重启保留。"; });
    private Task ReloadProfilesAsync() => RunBusyAsync(async () => { await _manager.ReloadProfilesAsync(); Notice = _manager.Current.Error?.Message ?? "Profile 已重新加载"; });
    private Task ApplyScenarioAsync() => RunBusyAsync(async () =>
    {
        if (_fake is null) return;
        _fake.SetScenario(SelectedScenario);
        var result = await _manager.GetCandidatesAsync();
        Candidates.Clear();
        foreach (var candidate in result.Candidates) Candidates.Add(candidate);
        Notice = $"Demo 场景：{SelectedScenario}";
    });
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private void OnUpdated(GameSessionSnapshot snapshot) => Dispatcher.UIThread.Post(() => Display(snapshot));
    private void Display(GameSessionSnapshot s)
    {
        StatusText = $"{s.State} / {s.SelectionKind} · 自动搜索 {(s.AutoDiscoveryEnabled ? "开" : "关")} · " +
            $"窗口绑定有效 {(s.Identity?.Verification == IdentityVerification.Verified ? "是" : "否")} · " +
            $"尺寸匹配 {(s.ProfileValidation?.Kind == ProfileValidationKind.Matched ? "是" : "否")} · 目标游戏未验证";
        var g = s.Geometry;
        Diagnostics = $"SessionId: {s.SessionId}\nBindingGeneration: {s.BindingGeneration}\nGeometryVersion: {s.GeometryVersion}\n" +
            $"WindowId: {s.Identity?.Id} · PID: {s.Identity?.ProcessId} · Identity: {s.Identity?.Verification}\n" +
            $"WindowBoundsPx: {g?.WindowBoundsPx}\nVisibleFrameBoundsPx: {g?.VisibleFrameBoundsPx} ({g?.VisibleFrameSource})\n" +
            $"ClientSizePx: {g?.ClientSizePx}\nClientOriginScreenPx: {g?.ClientOriginScreenPx}\nClientBoundsScreenPx: {g?.ClientBoundsScreenPx}\n" +
            $"MonitorBoundsPx: {g?.MonitorBoundsPx} · WorkAreaPx: {g?.WorkAreaPx} · MonitorId: {g?.MonitorId}\n" +
            $"TargetWindowDpi: {g?.TargetWindowDpi} · TargetAwareness: {g?.TargetAwareness} · CallerAwareness: {g?.CallerAwareness}\n" +
            $"GeometryValidity: {g?.Validity} · Sampled: {g?.ObservedAt:O} · ObservedAt: {s.ObservedAt:O}\n" +
            $"Profile: {s.ProfileValidation?.ProfileId} / {s.ProfileValidation?.Kind} · {s.ProfileValidation?.Reason}\n" +
            $"Foreground: {s.IsForeground} · Minimized: {s.IsMinimized} · BindingMode: {s.BindingMode} · TargetCompatibility: {s.TargetCompatibility}\n" +
            $"Error: {s.Error?.Code} · {s.Error?.Message} · Native: {s.Error?.NativeErrorCode}";
    }
    public void Dispose() => _manager.Updated -= OnUpdated;
}
