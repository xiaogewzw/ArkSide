using GameSidebar.App.Storage;
using GameSidebar.Core.Profiles;
using GameSidebar.Core.Sessions;

namespace GameSidebar.Application.Tests;

public sealed class StorageTests
{
    [Fact] public async Task Settings_round_trip_only_target_preferences()
    {
        var directory = TemporaryDirectory();
        try
        {
            var store = new SettingsStore(directory);
            await store.SaveAsync(new(1, new(Exe: "game.exe", TitleRule: "^Game"), true));
            var result = await store.LoadAsync();
            Assert.Equal("game.exe", result.EffectiveTarget.Exe);
            Assert.DoesNotContain("WindowId", await File.ReadAllTextAsync(store.FilePath));
            Assert.DoesNotContain("ProcessId", await File.ReadAllTextAsync(store.FilePath));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
    [Fact] public async Task Corrupt_settings_keep_a_backup_and_use_defaults()
    {
        var directory = TemporaryDirectory();
        try
        {
            var store = new SettingsStore(directory);
            await File.WriteAllTextAsync(store.FilePath, "{ broken json");
            var loaded = await store.LoadAsync();
            Assert.Null(loaded.EffectiveTarget.Exe);
            Assert.NotNull(store.RecoveryNotice);
            Assert.Single(Directory.GetFiles(directory, "settings.json.corrupt-*"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
    [Fact] public async Task Invalid_profile_json_returns_configuration_error()
    {
        var directory = TemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "bad.json"), "{ invalid");
            var result = await new JsonProfileRepository(directory).LoadAsync(CancellationToken.None);
            Assert.Equal(WindowErrorCode.InvalidConfiguration, result.Error?.Code);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
    [Fact] public async Task Target_file_takes_priority_and_empty_platform_does_not_fall_back()
    {
        var directory = TemporaryDirectory();
        try
        {
            var legacy = new TargetWindowRule(Exe: "legacy.exe");
            var store = new TargetConfigStore([], directory, mac: false);
            Assert.Equal("legacy.exe", (await store.LoadAsync(legacy)).Rule.Exe);
            await File.WriteAllTextAsync(store.DefaultPath, "{\"schemaVersion\":1,\"windows\":{\"executablePath\":null}}");
            var empty = await store.LoadAsync(legacy);
            Assert.Null(empty.Rule.Exe);
            Assert.False(empty.Rule.IsConfigured);
            await File.WriteAllTextAsync(store.DefaultPath, "{bad");
            var corrupt = await store.LoadAsync(legacy);
            Assert.Equal(WindowErrorCode.InvalidConfiguration, corrupt.Error?.Code);
            Assert.Null(corrupt.Rule.Exe);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact] public async Task Explicit_missing_target_does_not_use_legacy_settings()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "missing.json");
            var store = new TargetConfigStore(["--target-config", path], directory, mac: false);
            var result = await store.LoadAsync(new(Exe: "legacy.exe"));
            Assert.Equal(WindowErrorCode.InvalidConfiguration, result.Error?.Code);
            Assert.Null(result.Rule.Exe);
        }
        finally { Directory.Delete(directory, true); }
    }
    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "GameSidebarTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
