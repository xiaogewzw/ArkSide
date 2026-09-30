using System.Text.Json;
using GameSidebar.Core.Profiles;
using GameSidebar.Core.Sessions;
using GameSidebar.Platform.MacOS;

namespace GameSidebar.App.Storage;

public sealed record WindowsTarget(string? ExecutablePath = null, string? TitleRule = null,
    string? ClassRule = null, string? PreferredProfileId = null);
public sealed record MacTarget(string? AppBundlePath = null, string? BundleId = null,
    string? TitleRule = null, string? PreferredProfileId = null);
public sealed record TargetGameConfig(int SchemaVersion = 1, string? GameId = null, string? DisplayName = null,
    WindowsTarget? Windows = null, MacTarget? MacOS = null);
public sealed record TargetLoadResult(TargetWindowRule Rule, string Source, string? FilePath,
    TargetGameConfig? Config, WindowOperationError? Error = null);

public sealed class TargetConfigStore
{
    private readonly string? _explicitPath;
    private readonly string _defaultPath;
    private readonly bool _mac;
    private string? _activeSaveAsPath;

    public TargetConfigStore(string[] args, string? executableDirectory = null, bool? mac = null)
    {
        var index = Array.FindIndex(args, x => x.Equals("--target-config", StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            if (index + 1 >= args.Length || !Path.IsPathFullyQualified(args[index + 1]))
                throw new ArgumentException("--target-config 后需要绝对路径");
            _explicitPath = Path.GetFullPath(args[index + 1]);
        }
        _mac = mac ?? OperatingSystem.IsMacOS();
        var directory = executableDirectory ?? AppContext.BaseDirectory;
        if (_mac)
        {
            var bundle = new DirectoryInfo(directory);
            while (bundle is not null && !bundle.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) bundle = bundle.Parent;
            if (bundle?.Parent is not null) directory = bundle.Parent.FullName;
        }
        _defaultPath = Path.Combine(directory, "target-game.json");
    }

    public string DefaultPath => _defaultPath;

    public async Task<TargetLoadResult> LoadAsync(TargetWindowRule legacy, CancellationToken token = default)
    {
        var path = _activeSaveAsPath ?? _explicitPath ?? _defaultPath;
        if (!File.Exists(path))
        {
            if (_explicitPath is not null)
                return new(new(), path, path, null, new(WindowErrorCode.InvalidConfiguration, $"指定目标配置不存在：{path}"));
            return new(legacy, "旧 settings.json target（文件名匹配）", null, null);
        }
        try
        {
            await using var file = File.OpenRead(path);
            var config = await JsonSerializer.DeserializeAsync<TargetGameConfig>(file, JsonStorage.Options, token);
            if (config is null || config.SchemaVersion != 1) throw new JsonException("target-game.json schemaVersion 不受支持");
            var rule = _mac ? new TargetWindowRule(GameId: config.GameId, DisplayName: config.DisplayName,
                BundleId: config.MacOS?.BundleId, AppBundlePath: config.MacOS?.AppBundlePath,
                TitleRule: config.MacOS?.TitleRule, PreferredProfileId: config.MacOS?.PreferredProfileId)
                : new TargetWindowRule(GameId: config.GameId, DisplayName: config.DisplayName,
                ExecutablePath: config.Windows?.ExecutablePath, TitleRule: config.Windows?.TitleRule,
                ClassRule: config.Windows?.ClassRule, PreferredProfileId: config.Windows?.PreferredProfileId);
            var error = ProfileRules.Validate(rule);
            if (error is null && _mac && rule.AppBundlePath is not null && Directory.Exists(rule.AppBundlePath) &&
                !string.Equals(MacWindowService.ReadBundleId(rule.AppBundlePath), rule.BundleId, StringComparison.Ordinal))
                error = new(WindowErrorCode.InvalidConfiguration, "应用路径与 Bundle ID 指向不同应用；请重新选择目标");
            return new(rule, path, path, config, error);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new(new(), path, path, null, new(WindowErrorCode.InvalidConfiguration, $"目标配置加载失败：{e.Message}"));
        }
    }

    public async Task SaveAsync(TargetLoadResult previous, TargetWindowRule rule, string? saveAs = null, CancellationToken token = default)
    {
        var path = saveAs ?? previous.FilePath ?? _explicitPath ?? _defaultPath;
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("目标配置必须使用绝对路径");
        var source = previous.Config ?? new TargetGameConfig();
        var next = _mac ? source with { GameId = rule.GameId, DisplayName = rule.DisplayName,
            MacOS = new(rule.AppBundlePath, rule.BundleId, rule.TitleRule, rule.PreferredProfileId) }
            : source with { GameId = rule.GameId, DisplayName = rule.DisplayName,
                Windows = new(rule.ExecutablePath, rule.TitleRule, rule.ClassRule, rule.PreferredProfileId) };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, next, JsonStorage.Options, token);
            await stream.FlushAsync(token);
        }
        File.Move(temp, path, true);
        if (saveAs is not null) _activeSaveAsPath = path;
    }
}
