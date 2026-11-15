using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Automation;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Input;
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
    private const double DefaultHeight = 714;
    private const double CollapsedSize = 52;
    private Control _expandedContent = null!;
    private Control _collapsedContent = null!;
    private readonly Button _collapseButton;
    private Vector _collapsedOffset;
    private PixelPoint _expandedPosition;
    private double _expandedHeight = DefaultHeight;
    private bool _hasOpened;
    private PixelPoint? _resizeOrigin;
    private double _resizeStartHeight;
    private readonly GameSessionManager _sessions;
    private bool _closed;
    private bool _wasBound;
    public SidebarViewModel ViewModel { get; }
    public SidebarWindow(SidebarViewModel viewModel, GameSessionManager sessions,
        Func<Task> searchAgain)
    {
        ViewModel = viewModel; _sessions = sessions;
        DataContext = viewModel;
        Title = "ArkSide";
        Icon = AppIcon.Load();
        Width = ExpandedWidth; Height = DefaultHeight; MinHeight = 320;
        MinWidth = MaxWidth = ExpandedWidth;
        CanResize = true;
        Resized += (_, e) =>
        {
            if (!ViewModel.Collapsed && WindowState == WindowState.Normal &&
                e.ClientSize.Height >= 320 &&
                (e.Reason == WindowResizeReason.User ||
                 e.Reason == WindowResizeReason.Unspecified && _hasOpened))
                _expandedHeight = Math.Max(320, e.ClientSize.Height);
        };
        ShowActivated = true; ShowInTaskbar = true; Topmost = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var header = new Grid { ColumnDefinitions = new("36,*,36") };
        _collapseButton = CreateArrowButton(expand: false);
        header.Children.Add(_collapseButton);
        var title = new TextBlock
        {
            Text = "ArkSide", FontSize = 20, FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 1); header.Children.Add(title);
        var clear = Button("清空数据", nameof(SidebarViewModel.ClearRecognitionCommand));
        clear.Margin = new Thickness(0);
        clear.HorizontalAlignment = HorizontalAlignment.Left;
        var recommendations = new OperatorRecommendationControls(viewModel.Recommendations, "", clear,
            showDisabledNotes: false);
        var content = new Grid { RowDefinitions = new("*,Auto"), RowSpacing = 8 };
        var body = new StackPanel { Spacing = 12 };
        var connection = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 6 };
        var connectionDot = new Border
        {
            Width = 9, Height = 9, CornerRadius = new CornerRadius(5),
            VerticalAlignment = VerticalAlignment.Center
        };
        connection.Children.Add(connectionDot);
        var connectionStatus = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        connectionStatus.Bind(TextBlock.TextProperty, new Binding(nameof(SidebarViewModel.ConnectionStatus)));
        Grid.SetColumn(connectionStatus, 1); connection.Children.Add(connectionStatus);
        var search = new Button
        {
            Content = "重新连接", Command = new AsyncRelayCommand(searchAgain),
            FontSize = 12, Padding = new Thickness(8, 6), HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(search, 2); connection.Children.Add(search);
        body.Children.Add(connection);
        var recognize = Button("开始识别", nameof(SidebarViewModel.RecognizeOperatorsCommand));
        recognize.Width = 184; recognize.Height = 44;
        recognize.FontSize = 17; recognize.FontWeight = FontWeight.SemiBold;
        recognize.HorizontalAlignment = HorizontalAlignment.Center;
        recognize.HorizontalContentAlignment = HorizontalAlignment.Center;
        recognize.Background = Brush.Parse("#299C87"); recognize.Foreground = Brushes.White;
        recognize.Margin = new Thickness(0, 6, 0, 0);
        recognize.Bind(ContentControl.ContentProperty, new Binding(nameof(SidebarViewModel.RecognitionButtonText)));
        body.Children.Add(recognize);
        var recognition = new StackPanel { Spacing = 8 };
        var progress = new RecognitionBusyIndicator();
        progress.Bind(RecognitionBusyIndicator.IsActiveProperty, new Binding(nameof(SidebarViewModel.IsRecognizing)));
        recognition.Children.Add(progress);
        var recognitionStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 12
        };
        recognitionStatus.Bind(TextBlock.TextProperty, new Binding(nameof(SidebarViewModel.RecognitionSummary)));
        recognition.Children.Add(recognitionStatus);
        body.Children.Add(recognition);
        var filters = new Border { Child = recommendations.Filters };
        filters.Bind(IsEnabledProperty, new Binding(nameof(SidebarViewModel.IsConnected)));
        body.Children.Add(filters);
        body.Children.Add(recommendations.Results);
        void UpdateConnectionAppearance()
        {
            connectionDot.Background = Brush.Parse(viewModel.IsConnected ? "#299C87" : "#E5A52A");
        }
        UpdateConnectionAppearance();
        if (viewModel.IsMac)
        {
            var permission = Button("允许截图", nameof(SidebarViewModel.RequestPermissionCommand));
            permission.Bind(IsVisibleProperty, new Binding(nameof(SidebarViewModel.NeedsCapturePermission)));
            body.Children.Add(permission);
        }
        content.Children.Add(new ScrollViewer { Content = body,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var footer = new Border
        {
            BorderBrush = Brush.Parse("#40808080"), BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 8, 0, 0), Child = recommendations.Footer
        };
        Grid.SetRow(footer, 1); content.Children.Add(footer);
        var layout = new Grid { Margin = new Thickness(8), RowDefinitions = new("Auto,*"), RowSpacing = 8 };
        layout.Children.Add(header);
        Grid.SetRow(content, 1);
        layout.Children.Add(content);
        var resizeGrip = new Border
        {
            Height = 8, Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
            Child = new Border { Width = 32, Height = 2, Background = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        resizeGrip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(resizeGrip).Properties.IsLeftButtonPressed || ViewModel.Collapsed ||
                WindowState != WindowState.Normal) return;
            _resizeOrigin = resizeGrip.PointToScreen(e.GetPosition(resizeGrip));
            _resizeStartHeight = Bounds.Height;
            e.Pointer.Capture(resizeGrip);
            e.Handled = true;
        };
        resizeGrip.PointerMoved += (_, e) =>
        {
            if (_resizeOrigin is not { } origin) return;
            var current = resizeGrip.PointToScreen(e.GetPosition(resizeGrip));
            Height = _expandedHeight = Math.Max(MinHeight, _resizeStartHeight + (current.Y - origin.Y) / RenderScaling);
            e.Handled = true;
        };
        resizeGrip.PointerReleased += (_, e) =>
        {
            _resizeOrigin = null;
            e.Pointer.Capture(null);
        };
        resizeGrip.PointerCaptureLost += (_, _) => _resizeOrigin = null;
        var expanded = new Grid { RowDefinitions = new("*,Auto") };
        expanded.Children.Add(layout);
        Grid.SetRow(resizeGrip, 1);
        expanded.Children.Add(resizeGrip);
        _expandedContent = expanded;
        _collapsedContent = new Grid { Children = { CreateArrowButton(expand: true) } };
        Content = _expandedContent;
        void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SidebarViewModel.Collapsed))
                ApplyCollapsedState();
            if (e.PropertyName == nameof(SidebarViewModel.IsConnected))
                UpdateConnectionAppearance();
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
        Opened += (_, _) =>
        {
            _hasOpened = true;
            OnSession(_sessions.Current);
        };
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
            _expandedHeight = Height;
            Content = _collapsedContent;
            WindowDecorations = WindowDecorations.None;
            CanResize = false;
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            Background = Brushes.Transparent;
            TransparencyBackgroundFallback = Brushes.Transparent;
            MinHeight = 0;
            MinWidth = 0;
            MaxWidth = CollapsedSize;
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
            MaxWidth = ExpandedWidth;
            MinWidth = ExpandedWidth;
            CanResize = true;
            MinHeight = 320;
            Width = ExpandedWidth;
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
        double x, y;
        if (geometry.Placement is { Space: DesktopCoordinateSpace.MacDesktopPoints } placement)
        {
            var scale = Screens.Primary?.Scaling ?? 1;
            x = (placement.Frame.X + placement.Frame.Width) * scale;
            y = placement.Frame.Y * scale;
        }
        else
        {
            var rect = geometry.VisibleFrameBoundsPx;
            x = rect.Right; y = rect.Top;
        }
        _expandedPosition = new PixelPoint((int)Math.Round(x), (int)Math.Round(y));
        // Keep tracking the game, but don't resize/reposition a system-minimized window.
        if (WindowState == WindowState.Minimized) return;
        if (ViewModel.Collapsed) Height = CollapsedSize;
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
