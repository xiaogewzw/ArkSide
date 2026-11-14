using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using GameSidebar.App.ViewModels;
using GameSidebar.Core.Sessions;
using GameSidebar.Platform.MacOS;

namespace GameSidebar.App.Views;

public sealed class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        Title = "ArkSide · 窗口设置";
        Icon = AppIcon.Load();
        Width = 560; Height = 600; MinWidth = 460; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new Grid { Margin = new Thickness(20), RowDefinitions = new("Auto,*,Auto"), RowSpacing = 14 };
        var heading = new StackPanel { Spacing = 8 };
        heading.Children.Add(new TextBlock { Text = "窗口设置", FontSize = 22, FontWeight = FontWeight.SemiBold });
        heading.Children.Add(new TextBlock { Text = "默认自动连接明日方舟。需要更换目标时，请选择游戏窗口或应用。", TextWrapping = TextWrapping.Wrap });
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(MainViewModel.StatusText)));
        heading.Children.Add(status);
        root.Children.Add(heading);

        var body = new StackPanel { Spacing = 10 };
        var actions = new WrapPanel();
        actions.Children.Add(Button("刷新窗口", nameof(MainViewModel.RefreshCommand)));
        actions.Children.Add(Button("连接所选窗口", nameof(MainViewModel.BindCommand)));
        actions.Children.Add(Button("断开连接", nameof(MainViewModel.UnbindCommand)));
        body.Children.Add(actions);
        var list = new ListBox { Height = 170 };
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(MainViewModel.Candidates)));
        list.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(MainViewModel.SelectedCandidate)) { Mode = Avalonia.Data.BindingMode.TwoWay });
        list.ItemTemplate = new FuncDataTemplate<WindowCandidate>((candidate, _) =>
            new TextBlock { Text = string.IsNullOrWhiteSpace(candidate.Title) ? candidate.Executable ?? "游戏窗口" : candidate.Title, TextWrapping = TextWrapping.Wrap }, true);
        body.Children.Add(list);
        body.Children.Add(Button("记住所选游戏", nameof(MainViewModel.RememberCandidateCommand)));
        body.Children.Add(new TextBlock { Text = viewModel.IsMac ? "游戏应用" : "游戏程序路径", FontWeight = FontWeight.SemiBold });
        var path = new TextBox { PlaceholderText = viewModel.IsMac ? "选择游戏应用" : "例如 D:\\Games\\Game.exe" };
        path.Bind(TextBox.TextProperty, new Binding(viewModel.IsMac ? nameof(MainViewModel.AppBundlePath) : nameof(MainViewModel.Exe)) { Mode = Avalonia.Data.BindingMode.TwoWay });
        body.Children.Add(path);
        if (viewModel.IsMac)
        {
            var choose = new Button { Content = "选择游戏应用" };
            choose.Click += (_, _) =>
            {
                var selected = MacWindowService.ChooseApplication();
                if (selected is not null) viewModel.SetSelectedApp(selected);
            };
            body.Children.Add(choose);
        }
        var controls = new WrapPanel();
        controls.Children.Add(Button("保存并连接", nameof(MainViewModel.SaveSettingsCommand)));
        controls.Children.Add(Button("重新搜索", nameof(MainViewModel.StartDiscoveryCommand)));
        controls.Children.Add(Button("停止搜索", nameof(MainViewModel.StopDiscoveryCommand)));
        body.Children.Add(controls);
        var scroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var notice = new TextBlock { TextWrapping = TextWrapping.Wrap };
        notice.Bind(TextBlock.TextProperty, new Binding(nameof(MainViewModel.Notice)));
        Grid.SetRow(notice, 2); root.Children.Add(notice);
        Content = root;
    }
    private static Button Button(string title, string command)
    {
        var button = new Button { Content = title, Margin = new Thickness(0, 0, 6, 6) };
        button.Bind(Avalonia.Controls.Button.CommandProperty, new Binding(command));
        return button;
    }
}
