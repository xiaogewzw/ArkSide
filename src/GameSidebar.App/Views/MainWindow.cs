using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Media;
using GameSidebar.App.ViewModels;
using GameSidebar.Core.Sessions;
using GameSidebar.Platform.MacOS;
using DataBindingMode = Avalonia.Data.BindingMode;

namespace GameSidebar.App.Views;

public sealed class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        Title = "GameSidebar · 窗口绑定诊断";
        Width = 1180; Height = 780; MinWidth = 900; MinHeight = 620;
        var root = new Grid { Margin = new Thickness(18), RowDefinitions = new("Auto,Auto,Auto,*,Auto"), ColumnDefinitions = new("430,12,*") };
        var heading = new StackPanel { Spacing = 4 };
        heading.Children.Add(new TextBlock { Text = "GameSidebar 窗口绑定诊断", FontSize = 22, FontWeight = FontWeight.Bold });
        var mode = new TextBlock { FontSize = 15, Foreground = Brushes.DarkOrange };
        mode.Bind(TextBlock.TextProperty, new Binding(nameof(MainViewModel.ModeText)));
        heading.Children.Add(mode);
        var version = new TextBlock();
        version.Bind(TextBlock.TextProperty, new Binding(nameof(MainViewModel.VersionText)) { StringFormat = "版本 {0}" });
        heading.Children.Add(version);
        var source = new TextBlock { TextWrapping = TextWrapping.Wrap };
        source.Bind(TextBlock.TextProperty, new Binding(nameof(MainViewModel.ConfigSource)) { StringFormat = "目标配置：{0}" });
        heading.Children.Add(source);
        Grid.SetColumnSpan(heading, 3); root.Children.Add(heading);

        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12), FontWeight = FontWeight.SemiBold };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(MainViewModel.StatusText)));
        Grid.SetRow(status, 1); Grid.SetColumnSpan(status, 3); root.Children.Add(status);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 10) };
        actions.Children.Add(Button("刷新窗口", nameof(MainViewModel.RefreshCommand)));
        actions.Children.Add(Button("绑定所选", nameof(MainViewModel.BindCommand)));
        actions.Children.Add(Button("解绑", nameof(MainViewModel.UnbindCommand)));
        actions.Children.Add(Button("开始自动搜索", nameof(MainViewModel.StartDiscoveryCommand)));
        actions.Children.Add(Button("停止搜索", nameof(MainViewModel.StopDiscoveryCommand)));
        var busy = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        busy.Bind(IsVisibleProperty, new Binding(nameof(MainViewModel.IsBusy)));
        busy.Text = "执行中…"; actions.Children.Add(busy);
        Grid.SetRow(actions, 2); Grid.SetColumnSpan(actions, 3); root.Children.Add(actions);

        var left = new Grid { RowDefinitions = new("Auto,2*,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"), RowSpacing = 5 };
        left.Children.Add(new TextBlock { Text = "候选窗口（含排除原因）", FontWeight = FontWeight.Bold });
        var list = new ListBox { Height = 200 };
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(MainViewModel.Candidates)));
        list.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(MainViewModel.SelectedCandidate)) { Mode = DataBindingMode.TwoWay });
        list.ItemTemplate = new FuncDataTemplate<WindowCandidate>((candidate, _) =>
            new TextBlock { Text = $"{candidate.Title} · {candidate.Executable ?? "进程未知"} · PID {candidate.ProcessId} · {candidate.Id} · {candidate.ExecutablePath ?? candidate.BundleId} · " +
                $"{(candidate.IsMinimized ? "最小化" : "正常")} · {(candidate.IsSelectable ? "可选择" : "不可选择")} · {candidate.ExclusionReason}", TextWrapping = TextWrapping.Wrap }, true);
        Grid.SetRow(list, 1); left.Children.Add(list);
        AddLabel(left, 2, viewModel.IsMac ? "应用包路径（选择 .app 或从窗口回填）" : "目标 exe 完整路径（Windows）");
        AddEditor(left, 3, viewModel.IsMac ? nameof(MainViewModel.AppBundlePath) : nameof(MainViewModel.Exe),
            viewModel.IsMac ? "/Applications/Game.app" : "例如 D:\\Games\\Game.exe");
        AddLabel(left, 4, "标题正则（可选）");
        AddEditor(left, 5, nameof(MainViewModel.TitleRule), "可留空");
        if (!viewModel.IsMac)
        {
            AddLabel(left, 6, "类名正则（可选）");
            AddEditor(left, 7, nameof(MainViewModel.ClassRule), "可留空");
        }
        AddLabel(left, 8, "偏好 Profile ID");
        AddEditor(left, 9, nameof(MainViewModel.PreferredProfileId), "diagnostic-1920x1080");
        var settingsButtons = new WrapPanel { Orientation = Orientation.Horizontal };
        settingsButtons.Children.Add(Button("保存目标", nameof(MainViewModel.SaveSettingsCommand)));
        var saveAs = new Button { Content = "另存目标配置" };
        saveAs.Click += async (_, _) =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            { Title = "另存 target-game.json", SuggestedFileName = "target-game.json" });
            var path = file?.TryGetLocalPath();
            if (path is not null) await viewModel.SaveTargetAsAsync(path);
        };
        settingsButtons.Children.Add(saveAs);
        settingsButtons.Children.Add(Button("重新加载", nameof(MainViewModel.ReloadTargetCommand)));
        settingsButtons.Children.Add(Button("从所选窗口回填", nameof(MainViewModel.FillFromCandidateCommand)));
        settingsButtons.Children.Add(Button("应用 Profile 偏好", nameof(MainViewModel.SelectProfileCommand)));
        settingsButtons.Children.Add(Button("重载 Profile", nameof(MainViewModel.ReloadProfilesCommand)));
        Grid.SetRow(settingsButtons, 10); left.Children.Add(settingsButtons);
        if (viewModel.IsDemo)
        {
            var demo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var picker = new ComboBox { Width = 170 };
            picker.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(MainViewModel.Scenarios)));
            picker.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(MainViewModel.SelectedScenario)) { Mode = DataBindingMode.TwoWay });
            demo.Children.Add(picker);
            demo.Children.Add(Button("应用 Demo 场景", nameof(MainViewModel.ApplyScenarioCommand)));
            demo.Children.Add(Button("释放 A 读取", nameof(MainViewModel.ReleaseDelayedReadCommand)));
            Grid.SetRow(demo, 11); left.Children.Add(demo);
        }
        var instructions = new TextBlock { Text = "手动流程：刷新 → 选择窗口 → 绑定 → 截图。\n自动流程：选择应用或填 exe 完整路径 → 保存目标 → 自动搜索。", TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(instructions, 12); left.Children.Add(instructions);
        if (viewModel.IsMac)
        {
            AddLabel(left, 13, "Bundle ID");
            AddEditor(left, 14, nameof(MainViewModel.BundleId), "例如 com.example.game");
            var appActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var choose = new Button { Content = "选择应用" };
            choose.Click += (_, _) =>
            {
                var path = MacWindowService.ChooseApplication();
                if (path is not null) viewModel.SetSelectedApp(path);
            };
            appActions.Children.Add(choose);
            Grid.SetRow(appActions, 15); left.Children.Add(appActions);
        }
        var leftScroll = new ScrollViewer { Content = left };
        Grid.SetRow(leftScroll, 3); root.Children.Add(leftScroll);

        var right = new Grid { RowDefinitions = new("Auto,*,Auto") };
        right.Children.Add(new TextBlock { Text = viewModel.IsMac ? "实时诊断（macOS point；像素映射另列）" : "实时诊断（物理像素）", FontWeight = FontWeight.Bold });
        var details = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = FontFamily.Default };
        details.Bind(TextBox.TextProperty, new Binding(nameof(MainViewModel.Diagnostics)));
        Grid.SetRow(details, 1); right.Children.Add(details);
        var copy = new Button { Content = "复制诊断 JSON", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8) };
        copy.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null) await clipboard.SetValueAsync(DataFormat.Text, viewModel.DiagnosticJson);
        };
        Grid.SetRow(copy, 2); right.Children.Add(copy);
        Grid.SetColumn(right, 2); Grid.SetRow(right, 3); root.Children.Add(right);

        var notice = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8) };
        notice.Bind(TextBlock.TextProperty, new Binding(nameof(MainViewModel.Notice)));
        Grid.SetRow(notice, 4); Grid.SetColumnSpan(notice, 3); root.Children.Add(notice);
        Content = root;
    }
    private static Button Button(string title, string command)
    {
        var button = new Button { Content = title };
        button.Bind(Avalonia.Controls.Button.CommandProperty, new Binding(command));
        return button;
    }
    private static void AddLabel(Grid grid, int row, string text)
    {
        var label = new TextBlock { Text = text }; Grid.SetRow(label, row); grid.Children.Add(label);
    }
    private static void AddEditor(Grid grid, int row, string property, string watermark)
    {
        var editor = new TextBox { PlaceholderText = watermark };
        editor.Bind(TextBox.TextProperty, new Binding(property) { Mode = DataBindingMode.TwoWay });
        Grid.SetRow(editor, row); grid.Children.Add(editor);
    }
}
