using System.Text.Json;
using GameSidebar.Application.Recognition;

namespace GameSidebar.App.Storage;

public sealed record OperatorCatalog(IReadOnlyList<TaggedOperator> Operators,
    IReadOnlyList<string> ViceCovenants, IReadOnlyList<string> AllowedTags, string RecognitionSignature);

public sealed class OperatorCatalogStore(string? directory = null)
{
    public string DirectoryPath { get; } = directory ?? Path.Combine(AppContext.BaseDirectory, "recognition", "data");
    public string FilePath => Path.Combine(DirectoryPath, "干员详细信息.json");
    public string PortraitDirectory => Path.Combine(DirectoryPath, "portraits");

    public OperatorCatalog Load()
    {
        try
        {
            using var rules = JsonDocument.Parse(File.ReadAllText(Path.Combine(DirectoryPath, "干员禁用规则.json")));
            var options = rules.RootElement.GetProperty("covenantOptions");
            var main = Names(options.GetProperty("mainCovenant"), "主盟约选项");
            var vice = Names(options.GetProperty("viceCovenant"), "副盟约选项");
            using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
            // No whitelist means no visible tags. Never infer permissions from operator entries.
            var allowedTags = document.RootElement.TryGetProperty("allowedTags", out var allowed)
                ? Names(allowed, "allowedTags") : [];
            var allowedTagSet = allowedTags.ToHashSet(StringComparer.Ordinal);
            var operators = document.RootElement.GetProperty("operators").EnumerateArray().Select(item =>
            {
                var name = Required(item, "name");
                var portrait = Required(item, "portrait");
                if (portrait.IndexOfAny(['/', '\\']) >= 0 || portrait is "." or "..")
                    throw new InvalidDataException($"{name} 的 portrait 必须是头像文件名。");
                var op = new TaggedOperator(Required(item, "id"), name, portrait,
                    Names(item.GetProperty("mainCovenant"), $"{name}.mainCovenant"),
                    Names(item.GetProperty("viceCovenant"), $"{name}.viceCovenant"),
                    item.TryGetProperty("tag", out var tags)
                        ? Names(tags, $"{name}.tag").Where(allowedTagSet.Contains).ToArray() : []);
                if (op.MainCovenant.Except(main).Any() || op.ViceCovenant.Except(vice).Any())
                    throw new InvalidDataException($"{name} 的盟约不在盟约选项中。");
                if (item.TryGetProperty("tagCovenants", out var scopes))
                {
                    if (scopes.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException($"{name}.tagCovenants 必须是对象，例如 {{\"叠层核心\":[\"迅捷\"]}}。");
                    var mapping = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                    foreach (var entry in scopes.EnumerateObject())
                    {
                        var tag = entry.Name.Trim();
                        var covenants = Names(entry.Value, $"{name}.tagCovenants.{tag}");
                        if (covenants.Except(op.ViceCovenant).Any())
                            throw new InvalidDataException($"{name}.tagCovenants.{tag} 的盟约必须属于该干员的 viceCovenant。");
                        if (!mapping.TryAdd(tag, covenants))
                            throw new InvalidDataException($"{name}.tagCovenants 包含重复标签：{tag}。");
                    }
                    op = op with { TagCovenants = mapping };
                }
                if (item.TryGetProperty("rank", out var rank))
                {
                    if (rank.ValueKind != JsonValueKind.Number || !rank.TryGetInt32(out var value) || value is < 1 or > 6)
                        throw new InvalidDataException($"{name}.rank 必须是 1～6 的整数。");
                    op = op with { Rank = value };
                }
                return op;
            }).ToArray();
            if (operators.Length == 0 || operators.DistinctBy(o => o.Id).Count() != operators.Length ||
                operators.DistinctBy(o => o.Name).Count() != operators.Length)
                throw new InvalidDataException("干员库为空，或包含重复 ID / 名称。");
            // Tags, ranks and portraits can be reloaded without rerunning OCR; covenant/roster changes cannot.
            var signature = JsonSerializer.Serialize(new { main, vice, operators = operators.Select(o =>
                new { o.Id, o.Name, o.MainCovenant, o.ViceCovenant }) });
            return new(operators, vice, allowedTags, signature);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException("干员 JSON 格式无效；allowedTags 和 tag 必须是字符串数组，例如 [\"输出核心\"]。", e);
        }
    }

    private static string Required(JsonElement item, string key) =>
        item.GetProperty(key).GetString() is { } text && !string.IsNullOrWhiteSpace(text)
            ? text : throw new InvalidDataException($"干员的 {key} 不能为空。");

    private static string[] Names(JsonElement array, string field)
    {
        if (array.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{field} 必须是字符串数组。");
        var names = array.EnumerateArray().Select(x => x.GetString()).ToArray();
        if (names.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException($"{field} 不能包含空标签。");
        return names.Select(x => x!.Trim()).Distinct(StringComparer.Ordinal).ToArray();
    }
}
