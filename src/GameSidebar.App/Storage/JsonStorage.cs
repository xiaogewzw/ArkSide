using System.Text.Json;
using System.Text.Json.Serialization;
using GameSidebar.Application.Abstractions;
using GameSidebar.Core.Profiles;
using GameSidebar.Core.Sessions;

namespace GameSidebar.App.Storage;

public static class JsonStorage
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed record AppSettings(int SchemaVersion = 1, TargetWindowRule? Target = null, bool ShowExcludedWindows = true)
{
    public TargetWindowRule EffectiveTarget => Target ?? new TargetWindowRule();
}

public sealed class SettingsStore
{
    public SettingsStore(string? directoryPath = null) => DirectoryPath = directoryPath ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameSidebar");
    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public string? RecoveryNotice { get; private set; }

    public async Task<AppSettings> LoadAsync(CancellationToken token = default)
    {
        Directory.CreateDirectory(DirectoryPath);
        if (!File.Exists(FilePath)) return new();
        try
        {
            await using var file = File.OpenRead(FilePath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(file, JsonStorage.Options, token);
            if (settings is null || settings.SchemaVersion != 1) throw new JsonException("settings schemaVersion 不受支持");
            return settings;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            var backup = FilePath + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            try { File.Copy(FilePath, backup, overwrite: false); }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
            { backup = $"备份失败（原文件仍保留）：{backupError.Message}"; }
            RecoveryNotice = $"设置损坏：{e.Message}。已使用默认值；原文件备份：{backup}";
            return new();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken token = default)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporary = FilePath + ".tmp";
        await using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(file, settings, JsonStorage.Options, token);
            await file.FlushAsync(token);
        }
        File.Move(temporary, FilePath, overwrite: true);
    }
}

public sealed class JsonProfileRepository : IProfileRepository
{
    private readonly string _directory;
    public JsonProfileRepository(string directory) => _directory = directory;

    public async Task<ProfileLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        var profiles = new List<GameWindowProfile>();
        try
        {
            if (!Directory.Exists(_directory)) return new([], new(WindowErrorCode.InvalidConfiguration, $"Profile 目录不存在：{_directory}"));
            foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
            {
                await using var file = File.OpenRead(path);
                var profile = await JsonSerializer.DeserializeAsync<GameWindowProfile>(file, JsonStorage.Options, cancellationToken);
                if (profile is null) throw new JsonException($"Profile 为空：{path}");
                profiles.Add(profile);
            }
            if (profiles.Count == 0) return new([], new(WindowErrorCode.InvalidConfiguration, "没有可用 Profile"));
            return new(profiles, ProfileRules.ValidateProfiles(profiles));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        { return new([], new(WindowErrorCode.InvalidConfiguration, $"Profile 加载失败：{e.Message}")); }
    }
}
