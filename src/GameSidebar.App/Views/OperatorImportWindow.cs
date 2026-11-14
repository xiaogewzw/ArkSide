using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using GameSidebar.App.ViewModels;

namespace GameSidebar.App.Views;

public sealed class OperatorImportWindow : Window
{
    private readonly OperatorImportViewModel _model;
    private readonly OperatorRecommendationControls _recommendations;
    private readonly Button _import = new() { Content = "导入截图并识别", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _cancel = new() { Content = "取消识别" };
    private readonly Button _reload = new() { Content = "重新读取 JSON" };
    private readonly TextBlock _status = Label("");
    private readonly TextBlock _statistics = Label("");
    private readonly TextBlock _fileName = Label("");
    private readonly TextBlock _details = Label("");
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 3 };
    private readonly Image _preview = new() { MaxHeight = 190, Stretch = Stretch.Uniform };
    private byte[]? _previewBytes;
    private Bitmap? _previewBitmap;
    private bool _picking, _closing, _allowClose;
    private Task? _closeTask;

    public OperatorImportWindow(OperatorImportViewModel model)
    {
        _model = model;
        _recommendations = new(model, "请导入盟约确认页面的 PNG、JPG 或 BMP 截图。");
        Title = "ArkSide · 导入截图识别干员";
        Width = 325; Height = 760; MinWidth = 300; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { Margin = new Thickness(16), RowDefinitions = new("Auto,*,Auto"), RowSpacing = 14 };
        var header = new StackPanel { Spacing = 9 };
        header.Children.Add(new TextBlock { Text = "干员推荐", FontSize = 21, FontWeight = FontWeight.SemiBold });
        header.Children.Add(new TextBlock { Text = "ARKSIDE", FontSize = 11, Opacity = 0.65 });
        header.Children.Add(_import);
        var clear = new Button { Content = "清空本次数据" };
        clear.Click += (_, _) => model.Clear();
        header.Children.Add(clear);
        header.Children.Add(_progress);
        header.Children.Add(_status);
        _statistics.FontSize = 12; _statistics.Opacity = 0.75;
        header.Children.Add(_statistics);
        header.Children.Add(_cancel);
        header.Children.Add(_recommendations.Filters);
        root.Children.Add(header);

        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(_recommendations.Results);
        var screenshot = new StackPanel { Spacing = 8 };
        screenshot.Children.Add(_fileName); screenshot.Children.Add(_preview);
        body.Children.Add(new Expander { Header = "导入的截图", Content = screenshot, HorizontalAlignment = HorizontalAlignment.Stretch });
        var diagnostics = new StackPanel { Spacing = 8 };
        diagnostics.Children.Add(_details);
        diagnostics.Children.Add(Label("标签文件（修改后重新读取）："));
        diagnostics.Children.Add(new SelectableTextBlock { Text = model.CatalogPath, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        diagnostics.Children.Add(_reload);
        body.Children.Add(new Expander { Header = "数据与识别详情", Content = diagnostics, HorizontalAlignment = HorizontalAlignment.Stretch });
        var scroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        Grid.SetRow(_recommendations.Footer, 2); root.Children.Add(_recommendations.Footer);
        Content = root;

        _import.Click += async (_, _) => await PickScreenshotAsync();
        _cancel.Click += (_, _) => model.Cancel();
        _reload.Click += (_, _) => model.ReloadCatalog();
        model.PropertyChanged += OnChanged;
        Closing += (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;
            _ = CloseSafelyAsync();
        };
        Closed += (_, _) =>
        {
            model.PropertyChanged -= OnChanged;
            model.Dispose();
            _preview.Source = null; _previewBitmap?.Dispose();
            _recommendations.Dispose();
        };
        model.ReloadCatalog();
    }

    private void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Render();

    private void Render()
    {
        if (_closing) return;
        _import.IsEnabled = !_model.IsBusy && !_picking;
        _reload.IsEnabled = !_model.IsBusy;
        _cancel.IsVisible = _progress.IsVisible = _model.IsBusy;
        _status.Text = _model.Status;
        _statistics.Text = _model.Result is { } r
            ? $"干员库 {r.TotalOperators} 人 · 可用 {r.AvailableOperators} 人 · 禁用 {r.DisabledOperators.Count} 人" : "";
        _fileName.Text = _model.FileName;
        _details.Text = _model.Result is { } result
            ? $"禁用主盟约：{Names(result.DisabledMainCovenants)}\n禁用副盟约：{Names(result.DisabledViceCovenants)}\n\n禁用干员：{Names(result.DisabledOperators)}"
            : "导入截图后显示识别详情。";
        if (!ReferenceEquals(_previewBytes, _model.Screenshot))
        {
            _preview.Source = null; _previewBitmap?.Dispose(); _previewBitmap = null;
            _previewBytes = _model.Screenshot;
            if (_previewBytes is not null)
            {
                try { using var stream = new MemoryStream(_previewBytes, writable: false); _previewBitmap = Bitmap.DecodeToWidth(stream, 700); }
                catch (Exception) { /* The recognizer reports unsupported/corrupt input in Status. */ }
            }
            _preview.Source = _previewBitmap;
        }
    }

    private static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
    private static string Names(IReadOnlyList<string> values) => values.Count == 0 ? "无" : string.Join("、", values);
    private async Task PickScreenshotAsync()
    {
        if (_picking || _model.IsBusy || _closing) return;
        _picking = true; Render();
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "导入盟约确认页面截图", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("截图") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp"] }]
            });
            if (_closing || files.Count == 0) return;
            var file = files[0];
            await _model.ImportAsync(file.Name, async token =>
            { await using var stream = await file.OpenReadAsync(); return await ReadScreenshotAsync(stream, token); });
        }
        catch (Exception e) { if (!_closing) _status.Text = $"无法打开截图：{e.Message}"; }
        finally { _picking = false; if (!_closing) _import.IsEnabled = !_model.IsBusy; }
    }

    public Task ImportFromPathAsync(string path) => _model.ImportAsync(Path.GetFileName(path), async token =>
    { await using var stream = File.OpenRead(path); return await ReadScreenshotAsync(stream, token); });

    private static async Task<byte[]> ReadScreenshotAsync(Stream stream, CancellationToken token)
    {
        const int maxBytes = 32 * 1024 * 1024;
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + count > maxBytes) throw new InvalidDataException("截图超过 32 MB，请缩小后再导入。");
            output.Write(buffer, 0, count);
        }
        if (output.Length == 0) throw new InvalidDataException("截图文件为空。");
        return output.ToArray();
    }

    public Task CloseSafelyAsync() => _closeTask ??= FinishCloseAsync();
    private async Task FinishCloseAsync()
    {
        _closing = true; IsEnabled = false;
        await _model.StopAsync();
        _allowClose = true; Close();
    }
}
