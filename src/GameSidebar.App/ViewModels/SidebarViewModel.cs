using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSidebar.Application.Capture;
using GameSidebar.Application.Sessions;
using GameSidebar.Core.Sessions;
using GameSidebar.Platform.MacOS;

namespace GameSidebar.App.ViewModels;

public sealed class SidebarViewModel : ObservableObject, IDisposable
{
    private readonly CaptureCoordinator _capture;
    private readonly GameSessionManager _sessions;
    public OperatorImportViewModel Recommendations { get; }
    private bool _collapsed;
    private bool _disposed;
    private IReadOnlyList<string> _disabledOperators = [];
    private string _recognitionSummary = "打开游戏的盟约确认页面，点击“识别干员推荐”。";
    private bool _isRecognizing;
    private bool _needsCapturePermission;
    private string _connectionStatus = "正在搜索明日方舟应用…";
    private bool _canSearch = true;
    public SidebarViewModel(CaptureCoordinator capture, GameSessionManager sessions, OperatorImportViewModel recommendations)
    {
        _capture = capture; _sessions = sessions; Recommendations = recommendations;
        Recommendations.PropertyChanged += OnRecommendationsChanged;
        Recommendations.ReloadCatalog();
        _sessions.Updated += OnSession;
        ToggleCommand = new RelayCommand(() => Collapsed = !Collapsed);
        RequestPermissionCommand = new RelayCommand(RequestPermission);
        RecognizeOperatorsCommand = new AsyncRelayCommand(RecognizeOperatorsAsync,
            () => !_disposed && _sessions.Current.CanPreview);
        CancelRecognitionCommand = new RelayCommand(() => { RecognizeOperatorsCommand.Cancel(); Recommendations.Cancel(); });
        ClearRecognitionCommand = new RelayCommand(() =>
        {
            RecognizeOperatorsCommand.Cancel();
            Recommendations.Clear();
        });
    }
    public string ConnectionStatus { get => _connectionStatus; private set => SetProperty(ref _connectionStatus, value); }
    public bool CanSearch { get => _canSearch; private set => SetProperty(ref _canSearch, value); }
    public bool Collapsed { get => _collapsed; set => SetProperty(ref _collapsed, value); }
    public bool IsMac => OperatingSystem.IsMacOS();
    public bool NeedsCapturePermission { get => _needsCapturePermission; private set => SetProperty(ref _needsCapturePermission, value); }
    public IReadOnlyList<string> DisabledOperators
    {
        get => _disabledOperators;
        private set
        {
            if (SetProperty(ref _disabledOperators, value)) OnPropertyChanged(nameof(DisabledOperatorTitle));
        }
    }
    public string DisabledOperatorTitle => $"禁用干员（{DisabledOperators.Count}）";
    public string CovenantSummary => Recommendations.Result is { } result
        ? $"禁用主盟约：{string.Join("、", result.DisabledMainCovenants.DefaultIfEmpty("无"))}\n禁用副盟约：{string.Join("、", result.DisabledViceCovenants.DefaultIfEmpty("无"))}"
        : "识别后显示禁用盟约与名单。";
    public string RecognitionSummary { get => _recognitionSummary; private set => SetProperty(ref _recognitionSummary, value); }
    public bool IsRecognizing { get => _isRecognizing; private set => SetProperty(ref _isRecognizing, value); }
    public IRelayCommand ToggleCommand { get; }
    public IRelayCommand RequestPermissionCommand { get; }
    public IAsyncRelayCommand RecognizeOperatorsCommand { get; }
    public IRelayCommand CancelRecognitionCommand { get; }
    public IRelayCommand ClearRecognitionCommand { get; }

    private void OnRecommendationsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_disposed) return;
        DisabledOperators = Recommendations.Result?.DisabledOperators ?? [];
        NeedsCapturePermission = Recommendations.Failure == OperatorFailure.CapturePermission;
        OnPropertyChanged(nameof(CovenantSummary));
        RecognitionSummary = Recommendations.Result is { } r && Recommendations.Failure == OperatorFailure.None
            ? $"{Recommendations.Status}\n共 {r.TotalOperators} 名 · 禁用 {r.DisabledOperators.Count} 名 · 可选 {r.AvailableOperators} 名"
            : Recommendations.Status;
    }

    private async Task RecognizeOperatorsAsync(CancellationToken token)
    {
        if (_disposed || !_sessions.Current.CanPreview) return;
        var context = _sessions.Current;
        IsRecognizing = true;
        try
        {
            await Recommendations.ImportAsync("游戏画面", async captureToken =>
            {
                CaptureOutcome capture;
                try { capture = await _capture.CaptureOnceAsync(captureToken); }
                catch (OperationCanceledException) when (captureToken.IsCancellationRequested) { throw; }
                catch (UnauthorizedAccessException) { throw; }
                catch (Exception e) { throw new GameCaptureException(WindowErrorCode.CaptureFailed, e.Message); }
                captureToken.ThrowIfCancellationRequested();
                if (!IsCaptureCurrent(context))
                    throw new GameCaptureException(WindowErrorCode.Cancelled, "游戏窗口已变化");
                if (capture.Error is { } error) throw new GameCaptureException(error.Code, error.Message);
                var frame = capture.Frame ?? throw new GameCaptureException(WindowErrorCode.CaptureFailed, "未获取到游戏截图");
                if (frame.SessionId != context.SessionId || frame.BindingGeneration != context.BindingGeneration ||
                    frame.GeometryVersion != context.GeometryVersion)
                    throw new GameCaptureException(WindowErrorCode.Cancelled, "游戏窗口已变化");
                return frame.Png;
            }, token, () => IsCaptureCurrent(context), "正在截取当前游戏画面…");
        }
        finally { if (!_disposed) IsRecognizing = false; }
    }

    private bool IsCaptureCurrent(GameSessionSnapshot context)
    {
        var current = _sessions.Current;
        return !_disposed && current.CanPreview &&
            current.SessionId == context.SessionId && current.BindingGeneration == context.BindingGeneration &&
            current.GeometryVersion == context.GeometryVersion;
    }

    public async Task StopRecognitionAsync()
    {
        RecognizeOperatorsCommand.Cancel();
        Recommendations.Cancel();
        if (RecognizeOperatorsCommand.ExecutionTask is { } task) await task;
        await Recommendations.StopAsync();
    }

    private void OnSession(GameSessionSnapshot snapshot) => Dispatcher.UIThread.Post(() =>
    {
        if (_disposed) return;
        RecognizeOperatorsCommand.NotifyCanExecuteChanged();
        CanSearch = snapshot.Identity is null && snapshot.State is not (GameSessionState.Starting or GameSessionState.Binding);
        ConnectionStatus = snapshot.State switch
        {
            GameSessionState.Starting or GameSessionState.Binding or GameSessionState.Validating => "正在连接明日方舟应用…",
            GameSessionState.GameLost => "未找到明日方舟应用",
            GameSessionState.Faulted => "连接异常，请重新搜索或选择游戏窗口。",
            GameSessionState.NeedsSelection when snapshot.SelectionKind == SelectionKind.Window => "找到多个匹配窗口，请在“窗口设置”中选择。",
            _ when snapshot.Identity is not null && snapshot.IsMinimized => "已连接明日方舟 · 窗口已最小化",
            _ when snapshot.CanPreview => "已连接明日方舟",
            _ when snapshot.Identity is not null => "已连接明日方舟 · 暂时无法截图",
            GameSessionState.NotConfigured => "请在“窗口设置”中选择游戏应用。",
            _ => "未找到明日方舟应用"
        };
    });
    private void RequestPermission()
    {
        if (!IsMac) return;
        try
        {
            NeedsCapturePermission = !MacWindowService.ScreenPermission(true);
            RecognitionSummary = NeedsCapturePermission ? "没有截图权限" : "已允许截图，请重新识别。";
        }
        catch (Exception) { RecognitionSummary = "截图异常"; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RecognizeOperatorsCommand.Cancel();
        _sessions.Updated -= OnSession;
        Recommendations.PropertyChanged -= OnRecommendationsChanged;
        Recommendations.Dispose();
    }
}
