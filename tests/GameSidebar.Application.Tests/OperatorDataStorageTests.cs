using System.Text.Json;
using System.Text.Encodings.Web;
using GameSidebar.App.Storage;

namespace GameSidebar.Application.Tests;

public sealed class OperatorDataStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ArkSide data tests", Guid.NewGuid().ToString("N"));
    private string Bundled => Path.Combine(_root, "signed app", "recognition", "data");
    private string UserData => Path.Combine(_root, "用户配置", "recognition", "data");

    public OperatorDataStorageTests()
    {
        Directory.CreateDirectory(Path.Combine(Bundled, "portraits"));
        WriteDefaults("默认标签");
        File.WriteAllText(Path.Combine(Bundled, "portraits", "头像.png"), "bundled portrait");
    }

    [Fact]
    public void First_load_seeds_json_outside_the_bundle_and_keeps_portraits_in_the_bundle()
    {
        var store = new OperatorCatalogStore(UserData, Bundled);
        var catalog = store.Load();
        Assert.Equal(new[] { "默认标签" }, catalog.AllowedTags);
        Assert.Equal(Path.Combine(UserData, "干员详细信息.json"), store.FilePath);
        Assert.Equal(Path.Combine(Bundled, "portraits"), store.PortraitDirectory);
        Assert.False(Directory.Exists(Path.Combine(UserData, "portraits")));
        foreach (var name in new[] { "干员详细信息.json", "干员禁用规则.json" })
            Assert.Equal(File.ReadAllText(Path.Combine(Bundled, name)), File.ReadAllText(Path.Combine(UserData, name)));
        Assert.Empty(Directory.GetFiles(UserData, "*.tmp"));
    }

    [Fact]
    public void User_edits_survive_restarts_and_updated_bundled_defaults()
    {
        var store = new OperatorCatalogStore(UserData, Bundled);
        store.Load();
        var custom = File.ReadAllText(store.FilePath).Replace("默认标签", "自定义标签");
        File.WriteAllText(store.FilePath, custom);
        var customRules = File.ReadAllText(Path.Combine(UserData, "干员禁用规则.json")) + "\n";
        File.WriteAllText(Path.Combine(UserData, "干员禁用规则.json"), customRules);
        WriteDefaults("新版标签");
        var restarted = new OperatorCatalogStore(UserData, Bundled);
        Assert.Equal(new[] { "自定义标签" }, restarted.Load().Operators.Single().Tag);
        Assert.Equal(custom, File.ReadAllText(restarted.FilePath));
        Assert.Equal(customRules, File.ReadAllText(Path.Combine(UserData, "干员禁用规则.json")));
        Assert.Contains("新版标签", File.ReadAllText(Path.Combine(Bundled, "干员详细信息.json")));
    }

    [Fact]
    public void Restoring_one_missing_file_preserves_the_other_custom_file()
    {
        var store = new OperatorCatalogStore(UserData, Bundled);
        store.Load();
        File.WriteAllText(store.FilePath, File.ReadAllText(store.FilePath).Replace("默认标签", "自定义标签"));
        File.Delete(Path.Combine(UserData, "干员禁用规则.json"));
        Assert.Equal(new[] { "自定义标签" }, new OperatorCatalogStore(UserData, Bundled).Load().AllowedTags);
        Assert.True(File.Exists(Path.Combine(UserData, "干员禁用规则.json")));
    }

    [Fact]
    public void Invalid_user_json_reports_an_error_without_replacing_the_edit()
    {
        var store = new OperatorCatalogStore(UserData, Bundled);
        store.Load();
        File.WriteAllText(store.FilePath, "{ invalid");
        Assert.Throws<InvalidDataException>(() => new OperatorCatalogStore(UserData, Bundled).Load());
        Assert.Equal("{ invalid", File.ReadAllText(store.FilePath));
        Assert.Contains("默认标签", File.ReadAllText(Path.Combine(Bundled, "干员详细信息.json")));
    }

    [Fact]
    public void Custom_rules_and_roster_are_loaded_from_the_same_external_directory()
    {
        var store = new OperatorCatalogStore(UserData, Bundled);
        store.Load();
        foreach (var name in new[] { "干员详细信息.json", "干员禁用规则.json" })
        {
            var path = Path.Combine(UserData, name);
            File.WriteAllText(path, File.ReadAllText(path).Replace("副盟约", "用户副盟约"));
        }
        var catalog = new OperatorCatalogStore(UserData, Bundled).Load();
        Assert.Equal(new[] { "用户副盟约" }, catalog.ViceCovenants);
        Assert.Equal(new[] { "用户副盟约" }, catalog.Operators.Single().ViceCovenant);
    }

    [Fact]
    public async Task Concurrent_first_run_initialization_writes_complete_files()
    {
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            new OperatorCatalogStore(UserData, Bundled).EnsureDataFiles())));
        var store = new OperatorCatalogStore(UserData, Bundled);
        Assert.Single(store.Load().Operators);
        Assert.Empty(Directory.GetFiles(UserData, "*.tmp"));
    }

    private void WriteDefaults(string tag)
    {
        File.WriteAllText(Path.Combine(Bundled, "干员禁用规则.json"),
            """{"covenantOptions":{"mainCovenant":["主盟约"],"viceCovenant":["副盟约"]}}""");
        File.WriteAllText(Path.Combine(Bundled, "干员详细信息.json"), JsonSerializer.Serialize(new
        {
            allowedTags = new[] { tag },
            operators = new[] { new { id = "1", name = "干员", portrait = "头像.png", tag = new[] { tag },
                mainCovenant = Array.Empty<string>(), viceCovenant = new[] { "副盟约" }, rank = 5 } }
        }, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
