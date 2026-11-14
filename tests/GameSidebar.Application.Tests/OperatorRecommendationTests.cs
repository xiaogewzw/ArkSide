using Avalonia.Threading;
using GameSidebar.App.Development;
using GameSidebar.Application.Abstractions;
using GameSidebar.Application.Capture;
using GameSidebar.Application.Sessions;
using GameSidebar.Core.Profiles;
using GameSidebar.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using GameSidebar.App.Storage;
using GameSidebar.App.ViewModels;
using GameSidebar.Application.Recognition;

namespace GameSidebar.Application.Tests;

public sealed class OperatorRecommendationTests
{
    private static TaggedOperator Op(string id, string[] tags, string[] vice) =>
        new(id, id, id + ".png", [], vice, tags);

    [Fact]
    public void Rank_filters_across_covenants_then_intersects_bans_and_limit()
    {
        TaggedOperator[] ops = [
            Op("A", ["叠层核心"], ["奥术", "远见"]) with { Rank = 5,
                TagCovenants = new Dictionary<string, IReadOnlyList<string>> { ["叠层核心"] = new[] { "奥术" } } },
            Op("B", ["叠层核心"], ["远见"]) with { Rank = 5 },
            Op("C", ["叠层核心"], ["远见"]) with { Rank = 4 },
            Op("D", ["叠层核心"], []) with { Rank = 5 },
            Op("E", ["叠层核心"], ["远见"]) with { Rank = 5 }];
        var result = new OperatorRecognitionResult(["B", "C"], 5, 3, [], []);
        var all = Assert.Single(OperatorRecommendations.Select(ops, result, rank: 5, limit: 2));
        Assert.Equal(new[] { "A", "D" }, all.Operators.Select(o => o.Name));
        Assert.Equal(3, all.AvailableCount);
        Assert.Equal(1, all.DisabledCount);
        var scoped = Assert.Single(OperatorRecommendations.Select(ops, result, viceCovenant: "远见", rank: 5));
        Assert.Equal(new[] { "A", "E" }, scoped.Operators.Select(o => o.Name));
        Assert.Equal(1, scoped.DisabledCount);
        var banned = Assert.Single(OperatorRecommendations.Select(ops, result, rank: 4));
        Assert.Empty(banned.Operators);
        Assert.Contains("均被禁用", banned.EmptyMessage);
        var empty = Assert.Single(OperatorRecommendations.Select(ops, result, rank: 1));
        Assert.Equal(0, empty.DisabledCount);
        Assert.Contains("暂无可用干员", empty.EmptyMessage);
    }

    [Fact]
    public void Foresight_filter_includes_eyjafjalla_but_card_only_displays_aggregation()
    {
        var op = Op("纯烬艾雅法拉", ["叠层核心"], ["聚合", "远见"]) with
        {
            Rank = 5,
            TagCovenants = new Dictionary<string, IReadOnlyList<string>> { ["叠层核心"] = new[] { "聚合" } }
        };
        foreach (var vice in new string?[] { null, "聚合", "远见" })
        {
            var group = Assert.Single(OperatorRecommendations.Select([op], new([], 1, 1, [], []),
                viceCovenant: vice, rank: 5));
            Assert.Equal(new[] { "聚合" }, Assert.Single(group.Operators).DisplayCovenants(group.Tag));
        }
        Assert.Equal(new[] { "聚合", "远见" }, op.DisplayCovenants("输出核心"));
        var emptyScope = op with
        {
            TagCovenants = new Dictionary<string, IReadOnlyList<string>> { ["叠层核心"] = Array.Empty<string>() }
        };
        Assert.Empty(emptyScope.DisplayCovenants("叠层核心"));
    }

    [Fact]
    public void Default_selection_includes_every_available_operator_in_order()
    {
        var ops = Enumerable.Range(0, 10).Select(i => Op(i.ToString(), ["叠层核心"], ["远见"])).ToArray();
        var group = Assert.Single(OperatorRecommendations.Select(ops, new(["2"], 10, 9, [], [])));
        Assert.Equal(9, group.Operators.Count);
        Assert.Equal(ops.Where(o => o.Name != "2"), group.Operators);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("7")]
    [InlineData("2.5")]
    [InlineData("\"5\"")]
    [InlineData("null")]
    public void Catalog_rejects_invalid_rank(string rank)
    {
        using var fixture = new CatalogFixture();
        File.WriteAllText(fixture.Store.FilePath, File.ReadAllText(fixture.Store.FilePath)
            .Replace("\"portrait\":", $"\"rank\":{rank},\"portrait\":"));
        Assert.Throws<InvalidDataException>(() => fixture.Store.Load());
    }

    [Fact]
    public async Task Rank_reload_preserves_filters_and_recognition_and_refilters_results()
    {
        using var fixture = new CatalogFixture();
        fixture.Write(["叠层核心"]);
        Assert.Null(Assert.Single(fixture.Store.Load().Operators).Rank);
        var original = File.ReadAllText(fixture.Store.FilePath);
        File.WriteAllText(fixture.Store.FilePath, original.Replace("\"portrait\":", "\"rank\":5,\"portrait\":"));
        var recognizer = new StubRecognizer((_, _) => Task.FromResult(new OperatorRecognitionResult([], 1, 1, [], [])));
        using var vm = new OperatorImportViewModel(recognizer, fixture.Store);
        await vm.ImportAsync("sample.png", _ => Task.FromResult(new byte[] { 1 }));
        var result = vm.Result;
        var screenshot = vm.Screenshot;
        vm.Filter("叠层核心", null, 3, 5);
        Assert.Equal(5, Assert.Single(Assert.Single(vm.Groups).Operators).Rank);
        vm.Filter("叠层核心", "远见", 6);
        Assert.Equal(5, vm.SelectedRank);
        Assert.Single(Assert.Single(vm.Groups).Operators);
        File.WriteAllText(fixture.Store.FilePath, original.Replace("\"portrait\":", "\"rank\":4,\"portrait\":"));
        vm.ReloadCatalog();
        Assert.Empty(Assert.Single(vm.Groups).Operators);
        Assert.Equal(5, vm.SelectedRank);
        Assert.Equal("远见", vm.SelectedVice);
        Assert.Same(result, vm.Result);
        Assert.Same(screenshot, vm.Screenshot);
        Assert.Equal(1, recognizer.Calls);
        vm.Filter("叠层核心", "远见", 6, null);
        Assert.Equal(4, Assert.Single(Assert.Single(vm.Groups).Operators).Rank);
    }

    [Theory]
    [InlineData("{}", 1, 1)]
    [InlineData("{\"叠层核心\":[\"奥术\"]}", 1, 1)]
    [InlineData("{\"叠层核心\":[]}", 1, 1)]
    public void Catalog_tag_scopes_do_not_restrict_vice_filter(
        string scopes, int arcaneCount, int foresightCount)
    {
        using var fixture = new CatalogFixture();
        fixture.Write(["输出核心", "叠层核心"], vice: ["奥术", "远见"]);
        File.WriteAllText(fixture.Store.FilePath, File.ReadAllText(fixture.Store.FilePath)
            .Replace("\"portrait\":", $"\"tagCovenants\":{scopes},\"portrait\":"));
        var catalog = fixture.Store.Load();
        var result = new OperatorRecognitionResult([], 1, 1, [], []);
        foreach (var (vice, count) in new[] { ("奥术", arcaneCount), ("远见", foresightCount) })
        {
            var groups = OperatorRecommendations.Select(catalog.Operators, result, viceCovenant: vice);
            Assert.Single(groups.Single(g => g.Tag == "输出核心").Operators);
            Assert.Equal(count, groups.Single(g => g.Tag == "叠层核心").AvailableCount);
        }
        Assert.All(OperatorRecommendations.Select(catalog.Operators, result), g => Assert.Single(g.Operators));
        var banned = OperatorRecommendations.Select(catalog.Operators, new(["A"], 1, 0, [], []), viceCovenant: "奥术");
        Assert.Equal(arcaneCount, banned.Single(g => g.Tag == "叠层核心").DisabledCount);
        Assert.All(banned, g => Assert.Empty(g.Operators));
        fixture.Write(["输出核心", "叠层核心"], vice: ["奥术", "远见"]);
        Assert.Equal(catalog.RecognitionSignature, fixture.Store.Load().RecognitionSignature);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"叠层核心\":\"远见\"}")]
    [InlineData("{\"叠层核心\":[\"奥术\"]}")]
    public void Catalog_rejects_invalid_tag_scopes(string scopes)
    {
        using var fixture = new CatalogFixture();
        fixture.Write(["叠层核心"], vice: ["远见"]);
        File.WriteAllText(fixture.Store.FilePath, File.ReadAllText(fixture.Store.FilePath)
            .Replace("\"portrait\":", $"\"tagCovenants\":{scopes},\"portrait\":"));
        Assert.Throws<InvalidDataException>(() => fixture.Store.Load());
    }

    [Fact]
    public void Vice_filter_matches_any_value_keeps_groups_and_excludes_banned()
    {
        TaggedOperator[] ops = [Op("A", ["输出核心", "叠层核心"], ["奥术", "远见"]),
            Op("B", ["叠层核心"], ["远见"]), Op("C", ["输出核心"], []), Op("D", [], ["远见"])];
        var result = new OperatorRecognitionResult(["B"], 4, 3, [], []);
        var groups = OperatorRecommendations.Select(ops, result, viceCovenant: "远见");
        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Equal("A", Assert.Single(g.Operators).Name));
        Assert.Equal(1, groups[1].DisabledCount);
        var empty = OperatorRecommendations.Select(ops, result, viceCovenant: "坚守");
        Assert.Equal(2, empty.Count);
        Assert.All(empty, g => Assert.Empty(g.Operators));
        Assert.Equal(2, OperatorRecommendations.Select(ops, result)[0].AvailableCount);
    }

    [Fact]
    public void Limit_applies_after_ban_and_covenant_filter_in_json_order()
    {
        TaggedOperator[] ops = [Op("A", ["输出核心"], ["远见"]), Op("B", ["输出核心"], ["远见"]),
            Op("C", ["输出核心"], ["远见"])];
        var group = Assert.Single(OperatorRecommendations.Select(ops, new(["A"], 3, 2, [], []), limit: 1));
        Assert.Equal("B", Assert.Single(group.Operators).Name);
        Assert.Equal(2, group.AvailableCount);
        Assert.Equal(1, group.DisabledCount);
        Assert.Throws<InvalidDataException>(() => OperatorRecommendations.Select(ops, new(["Unknown"], 3, 2, [], [])));
    }

    [Fact]
    public void Catalog_supports_multiple_tags_and_rejects_string_tag()
    {
        using var fixture = new CatalogFixture();
        fixture.Write(["输出核心", "叠层核心"]);
        var catalog = fixture.Store.Load();
        Assert.Equal(new[] { "输出核心", "叠层核心" }, Assert.Single(catalog.Operators).Tag);
        var text = File.ReadAllText(fixture.Store.FilePath).Replace("\"tag\":[\"输出核心\",\"叠层核心\"]", "\"tag\":\"输出核心\"");
        File.WriteAllText(fixture.Store.FilePath, text);
        Assert.Throws<InvalidDataException>(() => fixture.Store.Load());
    }

    [Fact]
    public async Task Whitelist_hides_typos_and_reload_refilters_without_losing_screenshot()
    {
        using var fixture = new CatalogFixture();
        fixture.Write(["输出核心", "输出核芯", "叠层核心"], allowedTags: ["输出核心"]);
        var recognizer = new StubRecognizer((_, _) => Task.FromResult(new OperatorRecognitionResult([], 1, 1, [], [])));
        using var vm = new OperatorImportViewModel(recognizer, fixture.Store);
        await vm.ImportAsync("sample.png", _ => Task.FromResult(new byte[] { 1 }));
        var result = vm.Result;
        var screenshot = vm.Screenshot;
        Assert.Equal(new[] { "输出核心" }, vm.Tags);
        Assert.Equal("输出核心", Assert.Single(vm.Groups).Tag);
        Assert.Equal(new[] { "输出核心" }, Assert.Single(vm.Catalog!.Operators).Tag);
        vm.Filter("输出核心", null, 3);
        fixture.Write(["输出核心", "输出核芯", "叠层核心"], allowedTags: ["叠层核心"]);
        vm.ReloadCatalog();
        Assert.Null(vm.SelectedTag);
        Assert.Equal(new[] { "叠层核心" }, vm.Tags);
        Assert.Equal("叠层核心", Assert.Single(vm.Groups).Tag);
        Assert.Same(result, vm.Result); Assert.Same(screenshot, vm.Screenshot);
        Assert.Equal(1, recognizer.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Missing_or_empty_whitelist_never_falls_back_to_operator_tags(bool missing)
    {
        using var fixture = new CatalogFixture();
        fixture.Write(["输出核心", "拼错的标签"], allowedTags: []);
        if (missing)
            File.WriteAllText(fixture.Store.FilePath, File.ReadAllText(fixture.Store.FilePath).Replace("\"allowedTags\":[],", ""));
        var catalog = fixture.Store.Load();
        Assert.Empty(catalog.AllowedTags);
        Assert.Empty(Assert.Single(catalog.Operators).Tag);
    }

    [Fact]
    public async Task Malformed_whitelist_reload_preserves_previous_data()
    {
        using var fixture = new CatalogFixture();
        fixture.Write(["输出核心"]);
        var recognizer = new StubRecognizer((_, _) => Task.FromResult(new OperatorRecognitionResult([], 1, 1, [], [])));
        using var vm = new OperatorImportViewModel(recognizer, fixture.Store);
        await vm.ImportAsync("sample.png", _ => Task.FromResult(new byte[] { 1 }));
        var result = vm.Result;
        var catalog = vm.Catalog;
        var screenshot = vm.Screenshot;
        File.WriteAllText(fixture.Store.FilePath, File.ReadAllText(fixture.Store.FilePath)
            .Replace("\"allowedTags\":[\"输出核心\",\"叠层核心\"]", "\"allowedTags\":\"输出核心\""));
        Assert.Throws<InvalidDataException>(() => fixture.Store.Load());
        vm.ReloadCatalog();
        Assert.Equal("干员详细资料异常", vm.Status);
        Assert.Same(result, vm.Result); Assert.Same(catalog, vm.Catalog); Assert.Same(screenshot, vm.Screenshot);
        Assert.Single(vm.Groups);
    }

    [Fact]
    public async Task Tag_reload_refilters_existing_result_without_recognizing_again()
    {
        using var fixture = new CatalogFixture();
        var recognizer = new StubRecognizer((_, _) => Task.FromResult(new OperatorRecognitionResult([], 1, 1, [], [])));
        using var vm = new OperatorImportViewModel(recognizer, fixture.Store);
        await vm.ImportAsync("sample.png", _ => Task.FromResult(new byte[] { 1 }));
        Assert.NotNull(vm.Result);
        Assert.Empty(vm.Groups);
        fixture.Write(["输出核心"]);
        vm.ReloadCatalog();
        Assert.Equal("A", Assert.Single(Assert.Single(vm.Groups).Operators).Name);
        Assert.Equal(1, recognizer.Calls);
        fixture.Write(["输出核心"], ["奥术"]);
        vm.ReloadCatalog();
        Assert.NotNull(vm.Result);
        Assert.Single(vm.Groups);
        Assert.Equal(new[] { "远见" }, Assert.Single(vm.Catalog!.Operators).ViceCovenant);
        Assert.Contains("本次数据已保留", vm.Status);
    }

    [Fact]
    public async Task New_import_clears_previous_result_and_cancel_waits_for_worker()
    {
        using var fixture = new CatalogFixture();
        fixture.Write(["输出核心"]);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var recognizer = new StubRecognizer(async (_, token) =>
        {
            if (++calls == 2) { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); }
            return new([], 1, 1, [], []);
        });
        using var vm = new OperatorImportViewModel(recognizer, fixture.Store);
        await vm.ImportAsync("first.png", _ => Task.FromResult(new byte[] { 1 }));
        Assert.Single(vm.Groups);
        var pending = vm.ImportAsync("second.png", _ => Task.FromResult(new byte[] { 2 }));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Null(vm.Result);
        Assert.Empty(vm.Groups);
        Assert.True(vm.IsBusy);
        await vm.StopAsync();
        await pending;
        Assert.False(vm.IsBusy);
        Assert.Null(vm.Result);
        Assert.Contains("取消", vm.Status);
    }

    [Theory]
    [InlineData("permission", "没有截图权限", OperatorFailure.CapturePermission)]
    [InlineData("unauthorized", "没有截图权限", OperatorFailure.CapturePermission)]
    [InlineData("capture", "截图异常", OperatorFailure.Capture)]
    [InlineData("recognition", "请将游戏画面调整到所有盟约都显示出来", OperatorFailure.Recognition)]
    [InlineData("timeout", "请将游戏画面调整到所有盟约都显示出来", OperatorFailure.Recognition)]
    [InlineData("catalog", "干员详细资料异常", OperatorFailure.Catalog)]
    [InlineData("missing", "干员详细资料异常", OperatorFailure.Catalog)]
    public async Task Production_errors_are_classified_by_stage_without_exposing_details(
        string failure, string expected, OperatorFailure kind)
    {
        using var fixture = new CatalogFixture();
        fixture.Write(["输出核心"]);
        if (failure == "catalog") File.WriteAllText(fixture.Store.FilePath, "{broken");
        if (failure == "missing") File.Delete(fixture.Store.FilePath);
        var recognizer = new StubRecognizer((_, _) => failure switch
        {
            "recognition" => throw new InvalidDataException("底层识图细节"),
            "timeout" => throw new TimeoutException("底层超时细节"),
            _ => Task.FromResult(new OperatorRecognitionResult([], 1, 1, [], []))
        });
        using var vm = new OperatorImportViewModel(recognizer, fixture.Store);
        await vm.ImportAsync("游戏画面", _ => failure switch
        {
            "permission" => throw new GameCaptureException(WindowErrorCode.PermissionDenied, "底层权限细节"),
            "unauthorized" => throw new UnauthorizedAccessException("底层权限细节"),
            "capture" => throw new IOException("底层截图细节"),
            _ => Task.FromResult(new byte[] { 1 })
        });
        Assert.Equal(expected, vm.Status);
        Assert.Equal(kind, vm.Failure);
        Assert.Null(vm.Result);
        Assert.Empty(vm.Groups);
        Assert.False(vm.IsBusy);
        if (kind != OperatorFailure.Recognition) Assert.Equal(0, recognizer.Calls);
    }

    [Fact]
    public async Task Import_read_error_and_recognition_error_leave_no_old_recommendations()
    {
        using var fixture = new CatalogFixture();
        fixture.Write(["输出核心"]);
        var recognizer = new StubRecognizer((_, _) => Task.FromResult(new OperatorRecognitionResult([], 1, 1, [], [])));
        using var vm = new OperatorImportViewModel(recognizer, fixture.Store);
        await vm.ImportAsync("good.png", _ => Task.FromResult(new byte[] { 1 }));
        await vm.ImportAsync("missing.png", _ => throw new IOException("读取失败"));
        Assert.Null(vm.Result); Assert.Empty(vm.Groups); Assert.False(vm.IsBusy);
        Assert.Equal(1, recognizer.Calls);
        using var failed = new OperatorImportViewModel(new StubRecognizer((_, _) => throw new InvalidDataException("无法识别")), fixture.Store);
        await failed.ImportAsync("bad.png", _ => Task.FromResult(new byte[] { 1 }));
        Assert.Null(failed.Result); Assert.Empty(failed.Groups); Assert.Equal("请将游戏画面调整到所有盟约都显示出来", failed.Status);
    }

    [Fact]
    public async Task Roster_edit_during_recognition_is_not_published_as_a_valid_result()
    {
        using var fixture = new CatalogFixture();
        var recognizer = new StubRecognizer((_, _) =>
        {
            fixture.Write(["输出核心"], ["奥术"]);
            return Task.FromResult(new OperatorRecognitionResult([], 1, 1, [], []));
        });
        using var vm = new OperatorImportViewModel(recognizer, fixture.Store);
        await vm.ImportAsync("sample.png", _ => Task.FromResult(new byte[] { 1 }));
        Assert.Null(vm.Result); Assert.Equal("干员详细资料异常", vm.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Captured_screenshot_survives_window_changes_but_manual_clear_blocks_late_ocr(bool clear)
    {
        using var fixture = new CatalogFixture();
        fixture.Write(["输出核心"]);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<OperatorRecognitionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Deliberately ignore cancellation to verify that manual clear cannot be undone by late OCR.
        var recognizer = new StubRecognizer((_, _) => { entered.SetResult(); return completed.Task; });
        using var vm = new OperatorImportViewModel(recognizer, fixture.Store);
        var current = true;
        var task = vm.ImportAsync("游戏画面", _ => Task.FromResult(new byte[] { 1 }),
            isCurrent: () => current, loadingStatus: "正在截取当前游戏画面…");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        current = false;
        if (clear) vm.Clear();
        completed.SetResult(new([], 1, 1, [], []));
        await task;
        Assert.False(vm.IsBusy);
        if (clear)
        {
            Assert.Null(vm.Result); Assert.Empty(vm.Groups); Assert.Null(vm.Screenshot);
            Assert.Contains("本次数据已清空", vm.Status);
        }
        else
        {
            Assert.NotNull(vm.Result); Assert.Single(vm.Groups); Assert.NotNull(vm.Screenshot);
            Assert.Equal("识别完成", vm.Status);
        }
    }

    [Fact]
    public async Task Live_capture_changed_during_capture_does_not_start_ocr()
    {
        using var fixture = new CatalogFixture();
        var current = true;
        var recognizer = new StubRecognizer((_, _) => Task.FromResult(new OperatorRecognitionResult([], 1, 1, [], [])));
        using var vm = new OperatorImportViewModel(recognizer, fixture.Store);
        await vm.ImportAsync("游戏画面", _ => { current = false; return Task.FromResult(new byte[] { 1 }); },
            isCurrent: () => current);
        Assert.Equal(0, recognizer.Calls);
        Assert.Null(vm.Result); Assert.Null(vm.Screenshot);
    }

    [Fact]
    public async Task Manual_clear_removes_completed_results_and_allows_next_recognition()
    {
        using var fixture = new CatalogFixture();
        fixture.Write(["输出核心"]);
        var recognizer = new StubRecognizer((_, _) => Task.FromResult(new OperatorRecognitionResult([], 1, 1, [], [])));
        using var vm = new OperatorImportViewModel(recognizer, fixture.Store);
        await vm.ImportAsync("游戏画面", _ => Task.FromResult(new byte[] { 1 }));
        Assert.Single(vm.Groups);
        vm.Clear();
        Assert.Null(vm.Result); Assert.Empty(vm.Groups); Assert.Null(vm.Screenshot);
        vm.ReloadCatalog();
        Assert.Null(vm.Result); Assert.Empty(vm.Groups);
        await vm.ImportAsync("新窗口", _ => Task.FromResult(new byte[] { 2 }));
        Assert.Single(vm.Groups); Assert.Equal(2, recognizer.Calls);
    }

    [Fact]
    public async Task Live_command_token_cancels_worker_and_clears_busy_state()
    {
        using var fixture = new CatalogFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recognizer = new StubRecognizer(async (_, token) =>
        {
            entered.SetResult(); await Task.Delay(Timeout.Infinite, token);
            return new([], 1, 1, [], []);
        });
        using var vm = new OperatorImportViewModel(recognizer, fixture.Store);
        using var cancellation = new CancellationTokenSource();
        var task = vm.ImportAsync("游戏画面", _ => Task.FromResult(new byte[] { 1 }), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        await task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Null(vm.Result); Assert.False(vm.IsBusy); Assert.Contains("取消", vm.Status);
    }

    [Fact]
    public async Task Sidebar_keeps_recommendations_and_filters_after_game_exit_and_rebind()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Task.Run(() =>
        {
            var dispatcher = Dispatcher.UIThread;
            SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
            using var stop = new CancellationTokenSource();
            dispatcher.Post(async () =>
            {
                try
                {
                    using var fixture = new CatalogFixture();
                    fixture.Write(["输出核心"]);
                    var windows = new FakeWindowService();
                    var time = new ManualTimeProvider();
                    await using var manager = new GameSessionManager(windows, windows,
                        new SidebarProfiles(), NullLogger<GameSessionManager>.Instance, time);
                    await using var capture = new CaptureCoordinator(manager,
                        new FakeCaptureBackend());
                    var recognizer = new StubRecognizer((_, _) => Task.FromResult(new OperatorRecognitionResult([], 1, 1, [], [])));
                    using var sidebar = new SidebarViewModel(capture, manager, new OperatorImportViewModel(recognizer, fixture.Store));
                    await manager.InitializeAsync(new());
                    var candidate = Assert.Single((await windows.EnumerateAsync(default)).Candidates);
                    await manager.BindAsync(candidate);
                    await time.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5));
                    await sidebar.Recommendations.ImportAsync("游戏截图", _ => Task.FromResult(new byte[] { 1 }));
                    sidebar.Recommendations.Filter("输出核心", "远见", 6);
                    var result = sidebar.Recommendations.Result;
                    var screenshot = sidebar.Recommendations.Screenshot;
                    var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    manager.Updated += snapshot => { if (snapshot.State == GameSessionState.Unbound) lost.TrySetResult(); };
                    windows.SetScenario(DemoScenario.Disappeared);
                    time.Advance(TimeSpan.FromSeconds(1));
                    await lost.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                    Assert.Equal("未找到明日方舟应用", sidebar.ConnectionStatus);
                    Assert.True(sidebar.CanSearch);
                    Assert.False(sidebar.RecognizeOperatorsCommand.CanExecute(null));
                    Assert.Same(result, sidebar.Recommendations.Result);
                    Assert.Same(screenshot, sidebar.Recommendations.Screenshot);
                    Assert.Equal("输出核心", sidebar.Recommendations.SelectedTag);
                    Assert.Equal("远见", sidebar.Recommendations.SelectedVice);
                    Assert.Equal(6, sidebar.Recommendations.Limit);
                    Assert.Equal("A", Assert.Single(Assert.Single(sidebar.Recommendations.Groups).Operators).Name);
                    windows.SetScenario(DemoScenario.Restarted);
                    await manager.SetRuleAsync(new(Exe: "GameSidebar.TestWindow.exe"));
                    await manager.SearchNowAsync();
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                    Assert.True(sidebar.RecognizeOperatorsCommand.CanExecute(null));
                    Assert.Same(result, sidebar.Recommendations.Result);
                    Assert.Single(sidebar.Recommendations.Groups);
                    completed.TrySetResult();
                }
                catch (Exception e) { completed.TrySetException(e); }
                finally { stop.Cancel(); }
            });
            dispatcher.MainLoop(stop.Token);
            SynchronizationContext.SetSynchronizationContext(null);
        });
        await completed.Task;
    }

    private sealed class SidebarProfiles : IProfileRepository
    {
        public Task<ProfileLoadResult> LoadAsync(CancellationToken token) =>
            Task.FromResult(new ProfileLoadResult(
                [new(1, "diagnostic-1920x1080", ProfilePurpose.DiagnosticOnly,
                    null, new(1920, 1080), new(300, 48))]));
    }

    private sealed class StubRecognizer(Func<byte[], CancellationToken, Task<OperatorRecognitionResult>> run) : IOperatorRecognizer
    {
        public int Calls { get; private set; }
        public Task<OperatorRecognitionResult> RecognizeAsync(byte[] png, CancellationToken token)
        { Calls++; return run(png, token); }
    }

    private sealed class CatalogFixture : IDisposable
    {
        public OperatorCatalogStore Store { get; }
        public CatalogFixture()
        {
            var path = Directory.CreateTempSubdirectory("ArkSide-catalog-tests-").FullName;
            Store = new(path);
            File.WriteAllText(Path.Combine(path, "干员禁用规则.json"),
                JsonSerializer.Serialize(new { covenantOptions = new { mainCovenant = Array.Empty<string>(), viceCovenant = new[] { "奥术", "远见" } } }));
            Write([]);
        }
        public void Write(string[] tags, string[]? vice = null, string[]? allowedTags = null) => File.WriteAllText(Store.FilePath,
            JsonSerializer.Serialize(new { allowedTags = allowedTags ?? new[] { "输出核心", "叠层核心" }, operators = new[] { new { id = "1", name = "A", portrait = "A.png",
                mainCovenant = Array.Empty<string>(), viceCovenant = vice ?? ["远见"], tag = tags } } },
                new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        public void Dispose() => Directory.Delete(Store.DirectoryPath, recursive: true);
    }
}
