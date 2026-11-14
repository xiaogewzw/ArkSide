using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace GameSidebar.App.Views;

/// <summary>A busy pulse whose lifetime follows the operation and the visible window.</summary>
public sealed class RecognitionBusyIndicator : Border
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<RecognitionBusyIndicator, bool>(nameof(IsActive));
    private readonly Border _pulse = new() { Width = 64, Height = 4,
        Background = Brush.Parse("#299C87"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
    private readonly TranslateTransform _translation = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private Window? _window;
    private double _phase;

    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    public RecognitionBusyIndicator()
    {
        Height = 4;
        ClipToBounds = true;
        Background = Brush.Parse("#30299C87");
        IsVisible = false;
        _pulse.RenderTransform = _translation;
        Child = _pulse;
        _timer.Tick += (_, _) =>
        {
            _phase = (_phase + 0.025) % 1;
            _translation.X = _phase * (Bounds.Width + _pulse.Width) - _pulse.Width;
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty) Refresh();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null) _window.PropertyChanged += OnWindowChanged;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        if (_window is not null) _window.PropertyChanged -= OnWindowChanged;
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == IsVisibleProperty) Refresh();
    }

    private void Refresh()
    {
        _timer.Stop();
        IsVisible = IsActive;
        _phase = 0;
        _translation.X = -_pulse.Width;
        if (IsActive && _window is { IsVisible: true } && _window.WindowState != WindowState.Minimized)
            _timer.Start();
    }
}
