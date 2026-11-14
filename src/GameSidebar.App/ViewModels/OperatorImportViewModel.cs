using CommunityToolkit.Mvvm.ComponentModel;
using GameSidebar.Application.Capture;
using GameSidebar.Core.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GameSidebar.App.Storage;
using GameSidebar.Application.Recognition;

namespace GameSidebar.App.ViewModels;

public enum OperatorFailure { None, CapturePermission, Capture, Recognition, Catalog }

public sealed class OperatorImportViewModel(IOperatorRecognizer recognizer, OperatorCatalogStore store,
    ILogger<OperatorImportViewModel>? logger = null)
    : ObservableObject, IDisposable
{
    private readonly ILogger<OperatorImportViewModel> _logger = logger ?? NullLogger<OperatorImportViewModel>.Instance;
    private CancellationTokenSource? _operation;
    private Task? _running;
    private bool _disposed;
    private long _revision;
    public OperatorCatalog? Catalog { get; private set; }
    public OperatorRecognitionResult? Result { get; private set; }
    public IReadOnlyList<OperatorRecommendationGroup> Groups { get; private set; } = [];
    public string Status { get; private set; } = "打开游戏的盟约确认页面，点击“识别干员推荐”。";
    public OperatorFailure Failure { get; private set; }
    private enum ImportStage { Catalog, Capture, Recognition }
    public string FileName { get; private set; } = "尚未导入截图";
    public string CatalogPath => store.FilePath;
    public string PortraitDirectory => store.PortraitDirectory;
    public byte[]? Screenshot { get; private set; }
    public bool IsBusy { get; private set; }
    public string? SelectedTag { get; private set; }
    public string? SelectedVice { get; private set; }
    public int? SelectedRank { get; private set; }
    public int Limit { get; private set; } = int.MaxValue;
    public string[] Tags => Catalog?.AllowedTags.Where(t => Catalog.Operators.Any(o => o.Tag.Contains(t))).ToArray() ?? [];

    public void ReloadCatalog()
    {
        if (IsBusy || _disposed) return;
        try
        {
            Failure = OperatorFailure.None;
            var fresh = store.Load();
            if (Result is not null && Catalog?.RecognitionSignature != fresh.RecognitionSignature)
            {
                Status = "干员或盟约数据已变化；本次数据已保留，重新识别后使用新数据。";
                OnPropertyChanged(string.Empty);
                return;
            }
            else Status = Result is null ? "打开游戏的盟约确认页面，点击“识别干员推荐”。" : "标签与 Rank 已更新，识别结果已重新筛选。";
            Catalog = fresh;
            RefreshGroups();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Unable to load operator catalog");
            Failure = OperatorFailure.Catalog;
            Status = "干员详细资料异常";
        }
        OnPropertyChanged(string.Empty);
    }

    public void Filter(string? tag, string? vice, int limit)
        => Filter(tag, vice, limit, SelectedRank);

    public void Filter(string? tag, string? vice, int limit, int? rank)
    {
        if (_disposed) return;
        if (rank is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(rank));
        SelectedTag = tag; SelectedVice = vice; Limit = limit; SelectedRank = rank;
        RefreshGroups();
        OnPropertyChanged(string.Empty);
    }

    private void RefreshGroups()
    {
        if (!Tags.Contains(SelectedTag)) SelectedTag = null;
        if (Catalog?.ViceCovenants.Contains(SelectedVice) != true) SelectedVice = null;
        Groups = Result is not null && Catalog is not null
            ? OperatorRecommendations.Select(Catalog.Operators, Result, SelectedTag, SelectedVice, Limit, SelectedRank) : [];
    }

    // File imports and live captures share the same recognition/filtering pipeline.
    public Task ImportAsync(string name, Func<CancellationToken, Task<byte[]>> readImage,
        CancellationToken token = default, Func<bool>? isCurrent = null, string loadingStatus = "正在读取截图…")
    {
        if (IsBusy || _disposed) return Task.CompletedTask;
        _running = RunImportAsync(name, readImage, token, isCurrent, loadingStatus);
        return _running;
    }

    private async Task RunImportAsync(string name, Func<CancellationToken, Task<byte[]>> readImage,
        CancellationToken token, Func<bool>? isCurrent, string loadingStatus)
    {
        var revision = ++_revision;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        void CheckCurrent(bool checkCapture = false)
        {
            if (_disposed || revision != _revision || (checkCapture && isCurrent?.Invoke() == false)) cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
        }
        _operation = cancellation;
        IsBusy = true; Result = null; Groups = []; Screenshot = null; Failure = OperatorFailure.None;
        FileName = name; Status = loadingStatus;
        OnPropertyChanged(string.Empty);
        var stage = ImportStage.Catalog;
        try
        {
            CheckCurrent(checkCapture: true);
            var catalog = store.Load();
            Catalog = catalog;
            stage = ImportStage.Capture;
            var screenshot = await readImage(cancellation.Token);
            // Once accepted, this screenshot is independent of subsequent window changes.
            CheckCurrent(checkCapture: true);
            Screenshot = screenshot;
            Status = "正在离线识别禁用盟约…";
            OnPropertyChanged(string.Empty);
            stage = ImportStage.Recognition;
            var result = await recognizer.RecognizeAsync(Screenshot, cancellation.Token);
            CheckCurrent();
            stage = ImportStage.Catalog;
            var fresh = store.Load();
            if (fresh.RecognitionSignature != catalog.RecognitionSignature)
                throw new InvalidDataException("识别期间干员或盟约数据已变化，请重新识别。");
            Catalog = fresh;
            // Validate before publishing a successful result.
            _ = OperatorRecommendations.Select(fresh.Operators, result);
            Result = result;
            RefreshGroups();
            Status = "识别完成";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { if (revision == _revision) Status = "识别已取消，请重新识别。"; }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Operator recognition failed during {Stage}", stage);
            if (revision == _revision)
            {
                Result = null; Groups = [];
                Failure = stage switch
                {
                    ImportStage.Catalog => OperatorFailure.Catalog,
                    ImportStage.Capture when e is UnauthorizedAccessException or GameCaptureException { Code: WindowErrorCode.PermissionDenied }
                        => OperatorFailure.CapturePermission,
                    ImportStage.Capture => OperatorFailure.Capture,
                    _ => OperatorFailure.Recognition
                };
                Status = Failure switch
                {
                    OperatorFailure.CapturePermission => "没有截图权限",
                    OperatorFailure.Capture => "截图异常",
                    OperatorFailure.Catalog => "干员详细资料异常",
                    _ => "请将游戏画面调整到所有盟约都显示出来"
                };
            }
        }
        finally
        {
            IsBusy = false; _operation = null;
            if (!_disposed) OnPropertyChanged(string.Empty);
        }
    }

    public void Cancel() => _operation?.Cancel();
    public void Clear()
    {
        if (_disposed) return;
        ++_revision;
        Cancel();
        Result = null; Groups = []; Screenshot = null; Failure = OperatorFailure.None;
        FileName = "尚未导入截图";
        Status = "本次数据已清空，请重新识别。";
        OnPropertyChanged(string.Empty);
    }
    public async Task StopAsync()
    {
        Cancel();
        if (_running is { } running) await running;
    }
    public void Dispose() { _disposed = true; Cancel(); Screenshot = null; }
}
