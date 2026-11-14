using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Automation;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GameSidebar.App.ViewModels;
using GameSidebar.Application.Sessions;
using GameSidebar.Core.Geometry;
using GameSidebar.Core.Sessions;
using CommunityToolkit.Mvvm.Input;

namespace GameSidebar.App.Views;

public sealed class SidebarWindow : Window
{
    private const double ExpandedWidth = 300;
    private const double CollapsedSize = 52;
    private Control _expandedContent = null!;
    private Control _collapsedContent = null!;
    private readonly Button _collapseButton;
    private Vector _collapsedOffset;
    private PixelPoint _expandedPosition;
    private double _expandedHeight = 640;
    private double _expandedWidth = ExpandedWidth;
    private readonly GameSessionManager _sessions;
    private bool _closed;
    private bool _wasBound;
    public SidebarViewModel ViewModel { get; }
    public SidebarWindow(SidebarViewModel viewModel, GameSessionManager sessions,
        Func<Task> searchAgain, Action openSettings)
    {
        ViewModel = viewModel; _sessions = sessions;
        DataContext = viewModel;
        Title = "ArkSide";
        Icon = AppIcon.Load();
        Width = ExpandedWidth; Height = 640; MinHeight = 320;
        ShowActivated = true; ShowInTaskbar = true; Topmost = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _collapseButton = CreateArrowButton(expand: false);
        header.Children.Add(_collapseButton);
        var title = new TextBlock { Text = "干员推荐", FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
        title.Bind(IsVisibleProperty, new Binding(nameof(SidebarViewModel.Collapsed))
        { Converter = new InvertBooleanConverter() });
        header.Children.Add(title);
        var recommendations = new OperatorRecommendationControls(viewModel.Recommendations,
            "");
        var content = new Grid { RowDefinitions = new("*,Auto"), RowSpacing = 8 };
        content.Bind(IsVisibleProperty, new Binding(nameof(SidebarViewModel.Collapsed))
        { Converter = new InvertBooleanConverter() });
        var body = new StackPanel { Spacing = 12 };
        var connection = new StackPanel { Spacing = 6 };
        var connectionStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        connectionStatus.Bind(TextBlock.TextProperty, new Binding(nameof(SidebarViewModel.ConnectionStatus)));
        connection.Children.Add(connectionStatus);
        var connectionActions = new WrapPanel();
        var search = new Button { Content = "重新搜索", Command = new AsyncRelayCommand(searchAgain), Margin = new Thickness(0, 0, 6, 0) };
        search.Bind(IsVisibleProperty, new Binding(nameof(SidebarViewModel.CanSearch)));
        connectionActions.Children.Add(search);
        var settings = new Button { Content = "窗口设置" };
        settings.Click += (_, _) => openSettings();
        connectionActions.Children.Add(settings);
        connection.Children.Add(connectionActions);
        body.Children.Add(connection);
        var actions = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 4 };
        var recognize = Button("识别干员推荐", nameof(SidebarViewModel.RecognizeOperatorsCommand));
        recognize.HorizontalAlignment = HorizontalAlignment.Stretch;
        actions.Children.Add(recognize);
        var cancel = Button("取消", nameof(SidebarViewModel.CancelRecognitionCommand));
        cancel.Bind(IsVisibleProperty, new Binding(nameof(SidebarViewModel.IsRecognizing)));
        Grid.SetColumn(cancel, 1); actions.Children.Add(cancel);
        body.Children.Add(actions);
        body.Children.Add(Button("清空本次数据", nameof(SidebarViewModel.ClearRecognitionCommand)));
        var recognition = new StackPanel { Spacing = 5 };
        var recognitionStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        recognitionStatus.Bind(TextBlock.TextProperty, new Binding(nameof(SidebarViewModel.RecognitionSummary)));
        recognition.Children.Add(recognitionStatus);
        var progress = new ProgressBar { IsIndeterminate = true, Height = 4 };
        progress.Bind(IsVisibleProperty, new Binding(nameof(SidebarViewModel.IsRecognizing)));
        recognition.Children.Add(progress);
        body.Children.Add(recognition);
        body.Children.Add(recommendations.Filters);
        body.Children.Add(recommendations.Results);

        var data = new StackPanel { Spacing = 8 };
        var covenants = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        covenants.Bind(TextBlock.TextProperty, new Binding(nameof(SidebarViewModel.CovenantSummary)));
        data.Children.Add(covenants);
        var disabledTitle = new TextBlock { FontWeight = FontWeight.SemiBold };
        disabledTitle.Bind(TextBlock.TextProperty, new Binding(nameof(SidebarViewModel.DisabledOperatorTitle)));
        data.Children.Add(disabledTitle);
        var disabledOperators = new ListBox { MinHeight = 40, MaxHeight = 160, FontSize = 12 };
        disabledOperators.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(SidebarViewModel.DisabledOperators)));
        data.Children.Add(disabledOperators);
        body.Children.Add(new Expander { Header = "禁用盟约与干员", Content = data, HorizontalAlignment = HorizontalAlignment.Stretch });
        if (viewModel.IsMac)
        {
            var permission = Button("允许截图", nameof(SidebarViewModel.RequestPermissionCommand));
            permission.Bind(IsVisibleProperty, new Binding(nameof(SidebarViewModel.NeedsCapturePermission)));
            body.Children.Add(permission);
        }
        content.Children.Add(new ScrollViewer { Content = body,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Grid.SetRow(recommendations.Footer, 1); content.Children.Add(recommendations.Footer);
        var layout = new Grid { Margin = new Thickness(8), RowDefinitions = new("Auto,*"), RowSpacing = 8 };
        layout.Children.Add(header);
        Grid.SetRow(content, 1);
        layout.Children.Add(content);
        _expandedContent = layout;
        _collapsedContent = new Grid { Children = { CreateArrowButton(expand: true) } };
        Content = layout;
        void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SidebarViewModel.Collapsed))
                ApplyCollapsedState();
        }
        viewModel.PropertyChanged += OnViewModelChanged;
        Closed += (_, _) =>
        {
            _closed = true;
            _sessions.Updated -= OnSession;
            viewModel.PropertyChanged -= OnViewModelChanged;
            recommendations.Dispose();
        };
        _sessions.Updated += OnSession;
        Opened += (_, _) => OnSession(_sessions.Current);
    }
    private Button CreateArrowButton(bool expand)
    {
        var button = new Button
        {
            Width = 36, Height = 36,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Command = ViewModel.ToggleCommand,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Template = new FuncControlTemplate<Button>((_, _) => new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.Parse("#D0D0D0")),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18),
                BoxShadow = new BoxShadows(new BoxShadow
                    { OffsetY = 2, Blur = 5, Color = Color.Parse("#30000000") }),
                Child = new Avalonia.Controls.Shapes.Path
                {
                    Data = Geometry.Parse(expand
                        ? "M 1,8 L 15,8 M 9,2 L 15,8 L 9,14"
                        : "M 15,8 L 1,8 M 7,2 L 1,8 L 7,14"),
                    Stroke = new SolidColorBrush(Color.Parse("#333333")), StrokeThickness = 1.8,
                    Width = 16, Height = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            })
        };
        var label = expand ? "展开干员推荐" : "收缩干员推荐";
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, label);
        return button;
    }

    private PixelPoint CollapsedPosition => new(
        _expandedPosition.X + (int)Math.Round(_collapsedOffset.X * RenderScaling),
        _expandedPosition.Y + (int)Math.Round(_collapsedOffset.Y * RenderScaling));

    private void PositionCollapsedWindow()
    {
        var target = CollapsedPosition;
        Position = target;
        UpdateLayout();
        // Native window position and client origin can differ when decorations change.
        // Align the rendered content, rather than assuming the two origins coincide.
        var actual = _collapsedContent.PointToScreen(new Point(CollapsedSize / 2, CollapsedSize / 2));
        var radius = (int)Math.Round(CollapsedSize / 2 * RenderScaling);
        Position = new PixelPoint(Position.X + target.X + radius - actual.X,
            Position.Y + target.Y + radius - actual.Y);
    }

    private void ApplyCollapsedState()
    {
        if (ViewModel.Collapsed)
        {
            _expandedPosition = Position;
            // Measure the actual button center before removing the native title bar.
            // This keeps both arrows aligned regardless of title-bar height or DPI.
            var center = _collapseButton.PointToScreen(new Point(
                _collapseButton.Bounds.Width / 2, _collapseButton.Bounds.Height / 2));
            _collapsedOffset = new Vector(
                (center.X - _expandedPosition.X) / RenderScaling - CollapsedSize / 2,
                (center.Y - _expandedPosition.Y) / RenderScaling - CollapsedSize / 2);
            _expandedWidth = Width;
            _expandedHeight = Height;
            Content = _collapsedContent;
            WindowDecorations = WindowDecorations.None;
            CanResize = false;
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            Background = Brushes.Transparent;
            TransparencyBackgroundFallback = Brushes.Transparent;
            MinHeight = 0;
            Width = Height = CollapsedSize;
            PositionCollapsedWindow();
        }
        else
        {
            Content = _expandedContent;
            ClearValue(BackgroundProperty);
            ClearValue(TransparencyBackgroundFallbackProperty);
            TransparencyLevelHint = [];
            WindowDecorations = WindowDecorations.Full;
            CanResize = true;
            MinHeight = 320;
            Width = _expandedWidth;
            Height = _expandedHeight;
            Position = _expandedPosition;
        }
    }
    private void OnSession(GameSessionSnapshot snapshot) => Dispatcher.UIThread.Post(() =>
    {
        if (_closed) return;
        var bound = snapshot.Identity is not null && snapshot.State != GameSessionState.GameLost;
        if (_wasBound && !bound) ViewModel.Collapsed = false;
        _wasBound = bound;
        if (snapshot.SessionId is null || !snapshot.CanPreview || snapshot.Geometry is null ||
            (snapshot.Geometry.Placement?.Frame.IsValid != true && !snapshot.Geometry.IsUsable))
            return;
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
        _expandedPosition = new PixelPoint((int)Math.Round(x), (int)Math.Round(y));
        _expandedHeight = Math.Max(320, Math.Min(640, height));
        Height = ViewModel.Collapsed ? CollapsedSize : _expandedHeight;
        if (ViewModel.Collapsed) PositionCollapsedWindow();
        else Position = _expandedPosition;
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
