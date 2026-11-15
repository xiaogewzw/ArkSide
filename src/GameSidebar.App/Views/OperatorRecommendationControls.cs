using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GameSidebar.App.ViewModels;
using GameSidebar.Application.Recognition;

namespace GameSidebar.App.Views;

/// <summary>Shared read-only recommendation UI for imported screenshots and the live sidebar.</summary>
public sealed class OperatorRecommendationControls : IDisposable
{
    private sealed record Choice(string Text, string? Value)
    { public override string ToString() => Text; }
    private readonly OperatorImportViewModel _model;
    private readonly string _idlePrompt;
    private readonly bool _showDisabledNotes;
    private readonly TextBlock _empty = Label("");
    private readonly WrapPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly ComboBox _vice = new() { MinWidth = 155, MaxDropDownHeight = 336 };
    private readonly ComboBox _rank = new()
    {
        MinWidth = 155,
        ItemsSource = new[] { "全部等级", "Rank 1", "Rank 2", "Rank 3", "Rank 4", "Rank 5", "Rank 6" },
        SelectedIndex = 0
    };
    private readonly ComboBox _rows = new() { Width = 80, ItemsSource = new[] { "1 排", "2 排" }, SelectedIndex = 1 };
    private readonly StackPanel _groups = new() { Spacing = 18 };
    private readonly Dictionary<string, Bitmap?> _portraits = new(StringComparer.Ordinal);
    private readonly IBrush _accent = Brush.Parse("#299C87");
    private object? _catalog;
    private bool _rendering;
    public StackPanel Filters { get; } = new() { Spacing = 6 };
    public StackPanel Results { get; } = new() { Spacing = 12 };
    public Grid Footer { get; } = new() { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 6 };

    public OperatorRecommendationControls(OperatorImportViewModel model, string idlePrompt,
        Control? footerAction = null, bool showDisabledNotes = true)
    {
        _model = model; _idlePrompt = idlePrompt; _showDisabledNotes = showDisabledNotes;
        Filters.Children.Add(_tabs);
        var filter = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        filter.Children.Add(new TextBlock { Text = "副盟约", Width = 42, VerticalAlignment = VerticalAlignment.Center });
        filter.Children.Add(_vice); Filters.Children.Add(filter);
        var rankFilter = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        rankFilter.Children.Add(new TextBlock { Text = "Rank", Width = 42, VerticalAlignment = VerticalAlignment.Center });
        rankFilter.Children.Add(_rank); Filters.Children.Add(rankFilter);
        Results.Children.Add(_empty); Results.Children.Add(_groups);
        Footer.Children.Add(footerAction ?? new TextBlock { Text = "仅展示带标签的可用干员", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        var label = new TextBlock { Text = "显示", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1); Footer.Children.Add(label);
        Grid.SetColumn(_rows, 2); Footer.Children.Add(_rows);
        _vice.SelectionChanged += (_, _) => ApplyFilter();
        _rank.SelectionChanged += (_, _) => ApplyFilter();
        _rows.SelectionChanged += (_, _) => { if (!_rendering) Render(); };
        foreach (var selector in new[] { _vice, _rank, _rows })
            selector.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) =>
            {
                // Prevent accidental selection changes when closed, but let the
                // popup's ScrollViewer receive trackpad/wheel input when open.
                if (!selector.IsDropDownOpen) e.Handled = true;
            }, RoutingStrategies.Tunnel);
        model.PropertyChanged += OnChanged;
        Render();
    }

    private void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Render();
    private void Render()
    {
        _rendering = true;
        try
        {
            Filters.IsEnabled = _rows.IsEnabled = !_model.IsBusy;
            _empty.Text = _model.Result is null ? (_model.IsBusy ? "识别完成后显示可用干员。" : _idlePrompt)
                : _model.Tags.Length == 0 ? "暂无可推荐的干员。" : "";
            _empty.IsVisible = !string.IsNullOrEmpty(_empty.Text);
            _tabs.Children.Clear();
            foreach (var tag in new string?[] { null }.Concat(_model.Tags))
            {
                var button = new Button { Content = tag ?? "全部", Margin = new Thickness(0, 0, 6, 6), FontSize = 12 };
                if (tag == _model.SelectedTag) { button.BorderBrush = _accent; button.BorderThickness = new Thickness(2); }
                button.Click += (_, _) => _model.Filter(tag, _model.SelectedVice, int.MaxValue);
                _tabs.Children.Add(button);
            }
            var options = new[] { new Choice("全部副盟约", null) }
                .Concat((_model.Catalog?.ViceCovenants ?? []).Select(x => new Choice(x, x))).ToArray();
            _vice.ItemsSource = options;
            _vice.SelectedItem = options.FirstOrDefault(x => x.Value == _model.SelectedVice) ?? options[0];
            _rank.SelectedIndex = _model.SelectedRank ?? 0;
            _groups.Children.Clear();
            if (!ReferenceEquals(_catalog, _model.Catalog)) { DisposePortraits(); _catalog = _model.Catalog; }
            foreach (var group in _model.Groups) _groups.Children.Add(BuildGroup(group));
        }
        finally { _rendering = false; }
    }

    private void ApplyFilter()
    {
        if (!_rendering) _model.Filter(_model.SelectedTag, (_vice.SelectedItem as Choice)?.Value,
            int.MaxValue,
            _rank.SelectedIndex > 0 ? _rank.SelectedIndex : null);
    }

    private Control BuildGroup(OperatorRecommendationGroup group)
    {
        var panel = new StackPanel { Spacing = 8 };
        var heading = new Grid { ColumnDefinitions = new("*,Auto") };
        heading.Children.Add(new TextBlock { Text = "▎" + group.Tag, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        var count = new TextBlock { Text = $"可用 {group.AvailableCount} 人", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
        Grid.SetColumn(count, 1); heading.Children.Add(count); panel.Children.Add(heading);
        var rows = _rows.SelectedIndex == 1 ? 2 : 1;
        var grid = new Grid { Background = Brushes.Transparent };
        for (var row = 0; row < rows; row++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var col = 0; col < (group.Operators.Count + rows - 1) / rows; col++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(94)));
        var index = 0;
        foreach (var op in group.Operators)
        {
            var info = new StackPanel { Spacing = 4 };
            var bitmap = Portrait(op.Portrait);
            info.Children.Add(bitmap is not null
                ? new Image { Source = bitmap, Height = 65, Stretch = Stretch.UniformToFill }
                : new TextBlock { Text = "暂无头像", Height = 65, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            info.Children.Add(new TextBlock { Text = op.Name, FontSize = 12, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
            info.Children.Add(new TextBlock { Text = op.Rank is { } rank ? $"Rank {rank}" : "Rank 未配置",
                FontSize = 12, TextAlignment = TextAlignment.Center, Foreground = _accent });
            var covenants = op.DisplayCovenants(group.Tag);
            info.Children.Add(new TextBlock { Text = covenants.Count == 0 ? "无适用副盟约" : string.Join(" · ", covenants),
                FontSize = 11, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(3, 0, 3, 7) });
            var card = new Border { Child = info, Margin = new Thickness(3), CornerRadius = new CornerRadius(8), ClipToBounds = true,
                Background = new SolidColorBrush(Color.FromArgb(18, 128, 128, 128)), BorderBrush = new SolidColorBrush(Color.FromArgb(55, 128, 128, 128)), BorderThickness = new Thickness(1) };
            Grid.SetRow(card, index % rows);
            Grid.SetColumn(card, index / rows);
            grid.Children.Add(card);
            index++;
        }
        panel.Children.Add(group.Operators.Count > 0 ? HorizontalGallery(grid) : Label(group.EmptyMessage));
        var notes = new List<string>();
        if (_showDisabledNotes && group.DisabledCount > 0) notes.Add($"已隐藏 {group.DisabledCount} 名禁用干员");
        if (notes.Count > 0) panel.Children.Add(new TextBlock { Text = string.Join(" · ", notes), FontSize = 11, Opacity = 0.7 });
        return panel;
    }

    private static ScrollViewer HorizontalGallery(Control content)
    {
        var scroll = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Point? origin = null;
        double offset = 0;
        content.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(scroll).Properties.IsLeftButtonPressed)
            {
                origin = e.GetPosition(scroll);
                offset = scroll.Offset.X;
                e.Pointer.Capture(scroll);
                e.Handled = true;
            }
        };
        scroll.PointerMoved += (_, e) =>
        {
            if (origin is not { } start) return;
            scroll.Offset = new Vector(Math.Clamp(offset + start.X - e.GetPosition(scroll).X,
                0, Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width)), 0);
            e.Handled = true;
        };
        scroll.PointerReleased += (_, e) =>
        {
            if (origin is null) return;
            origin = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        };
        scroll.PointerCaptureLost += (_, _) => origin = null;
        return scroll;
    }

    private Bitmap? Portrait(string name)
    {
        if (_portraits.TryGetValue(name, out var cached)) return cached;
        Bitmap? bitmap = null;
        try { using var stream = File.OpenRead(Path.Combine(_model.PortraitDirectory, name)); bitmap = Bitmap.DecodeToWidth(stream, 200); }
        catch (Exception) { /* A missing portrait must not hide an otherwise valid recommendation. */ }
        _portraits[name] = bitmap;
        return bitmap;
    }
    private void DisposePortraits() { foreach (var bitmap in _portraits.Values) bitmap?.Dispose(); _portraits.Clear(); }
    private static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
    public void Dispose()
    {
        _model.PropertyChanged -= OnChanged;
        _groups.Children.Clear();
        DisposePortraits();
    }
}
