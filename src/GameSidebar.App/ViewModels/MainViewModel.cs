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
using GameSidebar.Platform.MacOS;

namespace GameSidebar.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly GameSessionManager _manager;
    private readonly SettingsStore _settings;
    private readonly TargetConfigStore _targetConfig;
    private readonly FakeWindowService? _fake;
    private AppSettings _currentSettings = new();
    private TargetLoadResult _targetLoad = new(new(), "尚未加载", null, null);
    private bool _busy;
    private WindowCandidate? _selectedCandidate;
    private string _statusText = "正在启动";
    private string _diagnostics = "";
    private string _notice = "";
    private string _exe = "";
    private string _bundleId = "";
    private string _appBundlePath = "";
    private string _configSource = "尚未加载";
    private string _titleRule = "";
    private string _classRule = "";
    private string _preferredProfileId = "diagnostic-1920x1080";
    private DemoScenario _selectedScenario = DemoScenario.SingleWindow;

    public MainViewModel(GameSessionManager manager, SettingsStore settings, TargetConfigStore targetConfig,
        FakeWindowService? fake, bool demo)
    {
        _manager = manager; _settings = settings; _targetConfig = targetConfig; _fake = fake; IsDemo = demo;
        ModeText = demo ? "DEMO 模式 · 模拟窗口" : OperatingSystem.IsMacOS() ? "macOS 真实窗口模式" : "Windows 真实窗口模式";
        VersionText = typeof(MainViewModel).Assembly.GetName().Version?.ToString() ?? "未知";
        _manager.Updated += OnUpdated;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        BindCommand = new AsyncRelayCommand(BindAsync);
        UnbindCommand = new AsyncRelayCommand(UnbindAsync);
        StartDiscoveryCommand = new AsyncRelayCommand(StartDiscoveryAsync);
        StopDiscoveryCommand = new AsyncRelayCommand(StopDiscoveryAsync);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
        ReloadTargetCommand = new AsyncRelayCommand(ReloadTargetAsync);
        FillFromCandidateCommand = new RelayCommand(FillFromCandidate);
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
    public string BundleId { get => _bundleId; set => SetProperty(ref _bundleId, value); }
    public string AppBundlePath { get => _appBundlePath; set => SetProperty(ref _appBundlePath, value); }
    public string ConfigSource { get => _configSource; private set => SetProperty(ref _configSource, value); }
    public bool IsMac => OperatingSystem.IsMacOS() && !IsDemo;
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
    public IAsyncRelayCommand ReloadTargetCommand { get; }
    public IRelayCommand FillFromCandidateCommand { get; }
    public IAsyncRelayCommand SelectProfileCommand { get; }
    public IAsyncRelayCommand ReloadProfilesCommand { get; }
    public IAsyncRelayCommand ApplyScenarioCommand { get; }
    public IRelayCommand ReleaseDelayedReadCommand { get; }
    public string DiagnosticJson => JsonSerializer.Serialize(new { version = VersionText, mode = ModeText,
        snapshot = _manager.Current, configSource = ConfigSource,
        target = new { executablePath = Exe, bundleId = BundleId, appBundlePath = AppBundlePath },
        settings = new { _currentSettings.SchemaVersion } }, JsonStorage.Options);

    public async Task InitializeAsync()
    {
        try { await InitializeCoreAsync(); }
        catch (Exception e) { Notice = $"初始化失败：{e.Message}"; StatusText = "Faulted · 初始化失败，请检查设置和日志"; }
    }
    private async Task InitializeCoreAsync()
    {
        _currentSettings = await _settings.LoadAsync();
        if (_settings.RecoveryNotice is not null) Notice = _settings.RecoveryNotice;
        _targetLoad = await _targetConfig.LoadAsync(_currentSettings.EffectiveTarget);
        ApplyLoadedTarget();
        await _manager.InitializeAsync(_targetLoad.Rule);
        if (_targetLoad.Error is not null) await _manager.ReportConfigErrorAsync(_targetLoad.Error);
        await RefreshAsync();
    }
    private void ApplyLoadedTarget()
    {
        ConfigSource = _targetLoad.Source;
        var target = _targetLoad.Rule;
        Exe = target.ExecutablePath ?? target.Exe ?? "";
        BundleId = target.BundleId ?? "";
        AppBundlePath = target.AppBundlePath ?? "";
        TitleRule = target.TitleRule ?? "";
        ClassRule = target.ClassRule ?? "";
        PreferredProfileId = target.PreferredProfileId ?? "";
        if (_targetLoad.Error is not null) Notice = _targetLoad.Error.Message;
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
        foreach (var candidate in result.Candidates.Where(x => IsMac ? x.IsSelectable : _currentSettings.ShowExcludedWindows || x.IsSelectable))
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
        var target = EditedTarget();
        var error = ValidateEditedTarget(target);
        if (error is not null) { Notice = error; return; }
        await _targetConfig.SaveAsync(_targetLoad, target);
        await ReloadSavedAsync(target);
    });
    public async Task SaveTargetAsAsync(string path)
    {
        var target = EditedTarget();
        var error = ValidateEditedTarget(target);
        if (error is not null) { Notice = error; return; }
        try
        {
            await _targetConfig.SaveAsync(_targetLoad, target, path);
            await ReloadSavedAsync(target);
            Notice += "；下次启动请用 --target-config 指向此文件";
        }
        catch (Exception e) { Notice = $"另存目标失败：{e.Message}"; }
    }
    private TargetWindowRule EditedTarget() => _targetLoad.Rule with { Exe = null,
            ExecutablePath = IsMac ? null : NullIfBlank(Exe),
            AppBundlePath = IsMac ? NullIfBlank(AppBundlePath) : null,
            BundleId = IsMac ? NullIfBlank(BundleId) : null,
            TitleRule = NullIfBlank(TitleRule), ClassRule = IsMac ? null : NullIfBlank(ClassRule),
            PreferredProfileId = NullIfBlank(PreferredProfileId) };
    private string? ValidateEditedTarget(TargetWindowRule target)
    {
        var error = ProfileRules.Validate(target);
        if (error is not null) return error.Message;
        if (IsMac && target.AppBundlePath is not null &&
            !string.Equals(MacWindowService.ReadBundleId(target.AppBundlePath), target.BundleId, StringComparison.Ordinal))
            return "应用路径与 Bundle ID 不一致，请重新选择应用或运行窗口";
        return null;
    }
    private async Task ReloadSavedAsync(TargetWindowRule target)
    {
        _targetLoad = await _targetConfig.LoadAsync(_currentSettings.EffectiveTarget);
        ApplyLoadedTarget();
        await _manager.SetRuleAsync(target);
        if (target.IsConfigured) await _manager.StartDiscoveryAsync();
        Notice = $"目标已保存到 {ConfigSource}";
    }
    private Task ReloadTargetAsync() => RunBusyAsync(async () =>
    {
        _targetLoad = await _targetConfig.LoadAsync(_currentSettings.EffectiveTarget);
        ApplyLoadedTarget();
        await _manager.SetRuleAsync(_targetLoad.Rule);
        if (_targetLoad.Error is not null) await _manager.ReportConfigErrorAsync(_targetLoad.Error);
        else if (_targetLoad.Rule.IsConfigured) await _manager.StartDiscoveryAsync();
        Notice = _targetLoad.Error?.Message ?? $"已重新加载 {ConfigSource}";
    });
    private void FillFromCandidate()
    {
        if (SelectedCandidate is null) { Notice = "请先选中运行窗口"; return; }
        if (IsMac)
        {
            AppBundlePath = SelectedCandidate.AppBundlePath ?? "";
            BundleId = SelectedCandidate.BundleId ?? "";
            Notice = "已从窗口回填应用身份；点击保存目标写入配置";
        }
        else
        {
            Exe = SelectedCandidate.ExecutablePath ?? "";
            Notice = Exe.Length == 0 ? "无法读取进程完整路径，不能自动匹配" : "已回填窗口进程完整路径；点击保存目标";
        }
    }
    public void SetSelectedApp(string path)
    {
        var bundleId = MacWindowService.ReadBundleId(path);
        if (bundleId is null) { Notice = "所选文件不是含 Bundle ID 的 .app 应用"; return; }
        AppBundlePath = path; BundleId = bundleId;
        Notice = "已选择应用；点击保存目标写入配置";
    }
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
            $"允许预览 {(s.CanPreview ? "是" : "否")} · " +
            $"尺寸匹配 {(s.ProfileValidation?.Kind == ProfileValidationKind.Matched ? "是" : "否")} · 目标游戏未验证";
        var g = s.Geometry;
        var platformGeometry = IsMac ? "Win32 窗口/Client/DWM/DPI：不适用；Mac Client 映射未知\n" :
            $"WindowBoundsPx: {g?.WindowBoundsPx}\nVisibleFrameBoundsPx: {g?.VisibleFrameBoundsPx} ({g?.VisibleFrameSource})\n" +
            $"ClientSizePx: {g?.ClientSizePx}\nClientOriginScreenPx: {g?.ClientOriginScreenPx}\nClientBoundsScreenPx: {g?.ClientBoundsScreenPx}\n" +
            $"MonitorBoundsPx: {g?.MonitorBoundsPx} · WorkAreaPx: {g?.WorkAreaPx} · MonitorId: {g?.MonitorId}\n" +
            $"TargetWindowDpi: {g?.TargetWindowDpi} · TargetAwareness: {g?.TargetAwareness} · CallerAwareness: {g?.CallerAwareness}\n";
        Diagnostics = $"ConfigSource: {ConfigSource}\nSessionId: {s.SessionId}\nBindingGeneration: {s.BindingGeneration}\nGeometryVersion: {s.GeometryVersion}\n" +
            $"WindowId: {s.Identity?.Id} · PID: {s.Identity?.ProcessId} · Identity: {s.Identity?.Verification}\n" +
            platformGeometry +
            $"Placement: {g?.Placement?.Space} · Frame: {g?.Placement?.Frame} · PixelsPerPoint: {g?.Placement?.PixelsPerPoint} · ClientMappingKnown: {g?.Placement?.ClientMappingKnown}\n" +
            $"GeometryValidity: {g?.Validity} · Sampled: {g?.ObservedAt:O} · ObservedAt: {s.ObservedAt:O}\n" +
            $"Profile: {s.ProfileValidation?.ProfileId} / {s.ProfileValidation?.Kind} · {s.ProfileValidation?.Reason}\n" +
            $"Foreground: {s.IsForeground} · Minimized: {s.IsMinimized} · BindingMode: {s.BindingMode} · TargetCompatibility: {s.TargetCompatibility}\n" +
            $"Error: {s.Error?.Code} · {s.Error?.Message} · Native: {s.Error?.NativeErrorCode}";
    }
    public void Dispose() => _manager.Updated -= OnUpdated;
}
