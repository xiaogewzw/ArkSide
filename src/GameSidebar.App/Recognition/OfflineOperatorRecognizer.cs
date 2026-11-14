using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using GameSidebar.Application.Recognition;
using Microsoft.Extensions.Logging;

namespace GameSidebar.App.Recognition;

/// <summary>Runs the local OpenCV/RapidOCR worker; no network or model downloads.</summary>
public sealed class OfflineOperatorRecognizer(ILogger<OfflineOperatorRecognizer> logger) : IOperatorRecognizer
{
    public async Task<OperatorRecognitionResult> RecognizeAsync(byte[] png, CancellationToken cancellationToken)
    {
        var start = CreateStartInfo();
        var temporary = Directory.CreateTempSubdirectory("ArkSide-recognition-");
        try
        {
            var imagePath = Path.Combine(temporary.FullName, "capture.png");
            await File.WriteAllBytesAsync(imagePath, png, cancellationToken);
            start.ArgumentList.Add(imagePath);
            start.ArgumentList.Add("--data-dir");
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "recognition", "data"));
            using var process = new Process { StartInfo = start };
            try
            {
                if (!process.Start()) throw new InvalidOperationException("无法启动离线识别组件");
            }
            catch (Win32Exception e)
            {
                logger.LogError(e, "Unable to start recognition worker {FileName}", start.FileName);
                throw new InvalidOperationException("无法启动离线识别组件，请检查程序包是否完整", e);
            }
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                await StopProcessAsync(process);
                await Task.WhenAll(stdout, stderr);
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("离线识别超时，请重新截图后重试");
            }
            finally
            {
                if (!process.HasExited) await StopProcessAsync(process);
            }
            var output = await stdout;
            var error = (await stderr).Trim();
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
            {
                logger.LogWarning("Recognition exited with {ExitCode}: {Error}", process.ExitCode, error);
                // The worker's normal validation errors are already phrased for the user.
                var message = error.StartsWith("识别失败：", StringComparison.Ordinal)
                    ? error[5..].Split('\n')[0].Trim() : "离线识别组件运行失败，请查看日志并检查组件是否完整";
                throw new InvalidOperationException(message.Length > 500 ? message[..500] : message);
            }
            if (!string.IsNullOrWhiteSpace(error))
                logger.LogInformation("Recognition diagnostics: {Diagnostics}", error);
            try { return ParseResult(output); }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or InvalidDataException or FormatException)
            {
                logger.LogError(e, "Invalid recognition worker result");
                throw new InvalidDataException("离线识别组件返回的结果无效，请重新识别", e);
            }
        }
        finally
        {
            try { temporary.Delete(recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { logger.LogWarning(e, "Unable to remove recognition temporary directory"); }
        }
    }

    private static async Task StopProcessAsync(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { } // Already exited.
        await process.WaitForExitAsync();
    }

    private static ProcessStartInfo CreateStartInfo()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "recognition");
        var worker = Path.Combine(directory, "worker", OperatingSystem.IsWindows() ? "ArkSide.Recognition.exe" : "ArkSide.Recognition");
        // Python's dotted native-library directories must live outside Contents/MacOS
        // so codesign treats them as resources rather than malformed code bundles.
        if (OperatingSystem.IsMacOS() && !File.Exists(worker))
            worker = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources",
                "recognition", "worker", "ArkSide.Recognition"));
#if ARKSIDE_PRODUCTION
        string? python = null;
#else
        var python = Environment.GetEnvironmentVariable("ARKSIDE_RECOGNITION_PYTHON");
#endif
        var start = new ProcessStartInfo
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        if (string.IsNullOrWhiteSpace(python) && File.Exists(worker))
        {
            start.FileName = worker;
            start.WorkingDirectory = directory;
        }
        else
        {
#if !ARKSIDE_PRODUCTION
            python = string.IsNullOrWhiteSpace(python) ? FindDevelopmentPython() : python;
#endif
            if (python is null || !Path.IsPathFullyQualified(python) || !File.Exists(python))
                throw new InvalidOperationException("未找到离线识别组件，请使用包含识别组件的完整程序包");
            start.FileName = python;
            start.WorkingDirectory = Path.Combine(directory, "python");
            start.ArgumentList.Add("-u");
            start.ArgumentList.Add("-m");
            start.ArgumentList.Add("tools.offline_recognition.cli");
        }
        start.Environment["PYTHONUTF8"] = "1";
        start.Environment["PYTHONNOUSERSITE"] = "1";
        start.Environment.Remove("PYTHONPATH");
        return start;
    }

    private static string? FindDevelopmentPython()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "GameSidebar.sln"))) continue;
            var relative = OperatingSystem.IsWindows() ? "Scripts/python.exe" : "bin/python";
            return Path.Combine(directory.FullName, ".venv-recognition", relative);
        }
        return null;
    }

    private static OperatorRecognitionResult ParseResult(string output)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var disabled = ReadNames(root.GetProperty("被禁用干员"));
        var available = ReadNames(root.GetProperty("可以选用干员"));
        var statistics = root.GetProperty("统计");
        var total = statistics.GetProperty("干员总数").GetInt32();
        var disabledCount = statistics.GetProperty("被禁用干员数").GetInt32();
        var availableCount = statistics.GetProperty("可以选用干员数").GetInt32();
        if (total <= 0 || disabledCount != disabled.Length || availableCount != available.Length ||
            (long)disabledCount + availableCount != total || disabled.Intersect(available, StringComparer.Ordinal).Any())
            throw new InvalidDataException("识别名单与统计不一致");
        var covenants = root.GetProperty("识别到的禁用盟约");
        return new(disabled, total, availableCount,
            ReadNames(covenants.GetProperty("mainCovenant")), ReadNames(covenants.GetProperty("viceCovenant")));
    }

    private static string[] ReadNames(JsonElement array)
    {
        var names = array.EnumerateArray().Select(item => item.GetString() ?? "").ToArray();
        if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new InvalidDataException("识别名单包含空白或重复名称");
        return names;
    }
}
