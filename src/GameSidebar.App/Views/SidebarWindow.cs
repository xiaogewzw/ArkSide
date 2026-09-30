using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GameSidebar.App.ViewModels;
using GameSidebar.Application.Sessions;
using GameSidebar.Core.Geometry;
using GameSidebar.Core.Sessions;

namespace GameSidebar.App.Views;

public sealed class SidebarWindow : Window
{
    private readonly GameSessionManager _sessions;
    private bool _shown;
    private bool _allowClose;
    public SidebarViewModel ViewModel { get; }
    public SidebarWindow(SidebarViewModel viewModel, GameSessionManager sessions)
    {
        ViewModel = viewModel; _sessions = sessions;
        DataContext = viewModel;
        Title = "GameSidebar · 截图预览";
        Width = 360; Height = 640; MinHeight = 320;
        ShowActivated = false; ShowInTaskbar = false; Topmost = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var toggle = Button("☰", nameof(SidebarViewModel.ToggleCommand));
        toggle.Width = 32; toggle.Height = 32;
        header.Children.Add(toggle);
        var title = new TextBlock { Text = "截图预览", FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
        title.Bind(IsVisibleProperty, new Binding(nameof(SidebarViewModel.Collapsed))
        { Converter = new InvertBooleanConverter() });
        header.Children.Add(title);
        var content = new Grid { RowDefinitions = new("Auto,Auto,*,Auto"), RowSpacing = 8 };
        content.Bind(IsVisibleProperty, new Binding(nameof(SidebarViewModel.Collapsed))
        { Converter = new InvertBooleanConverter() });
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(Button("截图", nameof(SidebarViewModel.ScreenshotCommand)));
        var save = new Button { Content = "保存 PNG" };
        save.Click += async (_, _) =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            { Title = "保存截图", SuggestedFileName = $"GameSidebar-{DateTime.Now:yyyyMMdd-HHmmss}.png" });
            var path = file?.TryGetLocalPath();
            if (path is not null) await ViewModel.SavePngAsync(path);
        };
        actions.Children.Add(save);
        actions.Children.Add(Button("开始预览", nameof(SidebarViewModel.StartPreviewCommand)));
        actions.Children.Add(Button("停止", nameof(SidebarViewModel.StopPreviewCommand)));
        content.Children.Add(actions);
        var rate = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        rate.Children.Add(new TextBlock { Text = "FPS (1–5)", VerticalAlignment = VerticalAlignment.Center });
        var fps = new NumericUpDown { Minimum = 1, Maximum = 5, Increment = 1, Width = 60 };
        fps.Bind(NumericUpDown.ValueProperty, new Binding(nameof(SidebarViewModel.Fps)) { Mode = Avalonia.Data.BindingMode.TwoWay });
        rate.Children.Add(fps);
        if (viewModel.IsMac) rate.Children.Add(Button("请求屏幕录制权限", nameof(SidebarViewModel.RequestPermissionCommand)));
        content.Children.Add(rate);
        Grid.SetRow(rate, 1);
        var image = new Image { Stretch = Stretch.Uniform };
        image.Bind(Image.SourceProperty, new Binding(nameof(SidebarViewModel.Image)));
        Grid.SetRow(image, 2);
        content.Children.Add(image);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(SidebarViewModel.Status)));
        Grid.SetRow(status, 3);
        content.Children.Add(status);
        var layout = new Grid { Margin = new Thickness(8), RowDefinitions = new("Auto,*"), RowSpacing = 8 };
        layout.Children.Add(header);
        Grid.SetRow(content, 1);
        layout.Children.Add(content);
        Content = layout;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SidebarViewModel.Collapsed))
                Width = viewModel.Collapsed ? 48 : 360;
        };
        Closed += (_, _) => _sessions.Updated -= OnSession;
        Closing += (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;
            ViewModel.Collapsed = true;
        };
    }
    public void CloseForShutdown() { _allowClose = true; Close(); }
    public void Attach(Window owner)
    {
        _sessions.Updated += OnSession;
        OnSession(_sessions.Current);
    }
    private void OnSession(GameSessionSnapshot snapshot) => Dispatcher.UIThread.Post(() =>
    {
        if (snapshot.SessionId is null || !snapshot.CanPreview || snapshot.Geometry is null ||
            (snapshot.Geometry.Placement?.Frame.IsValid != true && !snapshot.Geometry.IsUsable))
        { if (_shown) Hide(); return; }
        var geometry = snapshot.Geometry;
        double x, y, height;
        if (geometry.Placement is { Space: DesktopCoordinateSpace.MacDesktopPoints } placement)
        {
            var scale = Screens.Primary?.Scaling ?? 1;
            x = (placement.Frame.X + placement.Frame.Width) * scale;
            y = placement.Frame.Y * scale;
            height = placement.Frame.Height;
        }
        else
        {
            var rect = geometry.VisibleFrameBoundsPx;
            x = rect.Right; y = rect.Top;
            height = rect.Height / (Screens.Primary?.Scaling ?? 1);
        }
        Position = new PixelPoint((int)Math.Round(x), (int)Math.Round(y));
        Height = Math.Max(320, Math.Min(640, height));
        if (!_shown) { Show(); _shown = true; }
        else if (!IsVisible) Show();
    });
    private static Button Button(string label, string property)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 4, 4) };
        button.Bind(Avalonia.Controls.Button.CommandProperty, new Binding(property));
        return button;
    }
    private sealed class InvertBooleanConverter : Avalonia.Data.Converters.IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is not true;
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is not true;
    }
}
