using System.Text.Encodings.Web;
using System.Text.Json;
using GameSidebar.App.Recognition;
using GameSidebar.App.Storage;
using Microsoft.Extensions.Logging.Abstractions;

string? dataDirectory = null;
if (args.Length >= 2 && args[0] == "--data-dir")
{
    dataDirectory = Path.GetFullPath(args[1]);
    args = args[2..];
}
if (args.Length < 4 || args.Length % 2 != 0)
{
    Console.Error.WriteLine("用法：RecognitionCheck [--data-dir 外部数据目录] 主程序目录 报告路径 截图1 预期JSON1 [截图2 预期JSON2 ...]");
    return 2;
}

// This standalone check must resolve the worker/data beside the published app,
// not beside its own test executable. No Avalonia window or game is started.
var appDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[0]));
AppDomain.CurrentDomain.SetData("APP_CONTEXT_BASE_DIRECTORY", appDirectory + Path.DirectorySeparatorChar);
if (Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory) != appDirectory)
    throw new InvalidOperationException("验证工具未能使用指定的发布包目录");
var bundledDirectory = Path.Combine(appDirectory, "recognition", "data");
var store = dataDirectory is null ? new OperatorCatalogStore(bundledDirectory)
    : new OperatorCatalogStore(dataDirectory, bundledDirectory);
var catalog = store.Load();
var recognizer = new OfflineOperatorRecognizer(NullLogger<OfflineOperatorRecognizer>.Instance, store);
var records = new List<object>();
for (var i = 2; i < args.Length; i += 2)
{
    using var expected = JsonDocument.Parse(await File.ReadAllTextAsync(args[i + 1]));
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    var actual = await recognizer.RecognizeAsync(await File.ReadAllBytesAsync(args[i]), timeout.Token);
    var root = expected.RootElement;
    if (catalog.Operators.Count != actual.TotalOperators)
        throw new InvalidOperationException("推荐列表与识别组件未使用同一份干员数据");
    string[] Names(JsonElement value) => value.EnumerateArray().Select(x => x.GetString()!).ToArray();
    if (!actual.DisabledOperators.SequenceEqual(Names(root.GetProperty("被禁用干员"))) ||
        !actual.DisabledMainCovenants.SequenceEqual(Names(root.GetProperty("识别到的禁用盟约").GetProperty("mainCovenant"))) ||
        !actual.DisabledViceCovenants.SequenceEqual(Names(root.GetProperty("识别到的禁用盟约").GetProperty("viceCovenant"))) ||
        actual.TotalOperators != root.GetProperty("统计").GetProperty("干员总数").GetInt32() ||
        actual.AvailableOperators != root.GetProperty("统计").GetProperty("可以选用干员数").GetInt32())
        throw new InvalidOperationException($"发布包 C# 识别服务结果不匹配：{args[i]}");
    records.Add(new { image = Path.GetFileName(args[i]), passed = true,
        total = actual.TotalOperators, disabled = actual.DisabledOperators.Count, available = actual.AvailableOperators });
}
var report = JsonSerializer.Serialize(new { passed = true, bridge = "Published OfflineOperatorRecognizer + bundled worker",
    data_source = dataDirectory is null ? "bundled" : "external", catalog_operators = catalog.Operators.Count,
    catalog_tags = catalog.AllowedTags, portraits_from_bundle = store.PortraitDirectory == Path.Combine(bundledDirectory, "portraits"),
    cases = records },
    new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
var reportPath = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
await File.WriteAllTextAsync(reportPath, report);
Console.WriteLine(report);
return 0;
