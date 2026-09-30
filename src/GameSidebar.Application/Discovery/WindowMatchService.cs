using System.Text.RegularExpressions;
using GameSidebar.Core.Profiles;
using GameSidebar.Core.Sessions;

namespace GameSidebar.Application.Discovery;

public sealed record WindowMatchResult(IReadOnlyList<WindowCandidate> Candidates, WindowOperationError? Error = null);
public static class WindowMatchService
{
    public static WindowMatchResult Match(TargetWindowRule rule, IReadOnlyList<WindowCandidate> candidates)
    {
        var error = ProfileRules.Validate(rule);
        if (error is not null) return new([], error);
        if (!rule.IsConfigured) return new([], new(WindowErrorCode.NoConfiguration, "未配置目标，可手动选择窗口"));
        try
        {
            var title = CreateRegex(rule.TitleRule);
            var className = CreateRegex(rule.ClassRule);
            var matches = candidates.Where(c => c.IsAutoEligible && MatchesIdentity(rule, c) &&
                (title is null || title.IsMatch(c.Title)) &&
                (className is null || className.IsMatch(c.ClassName))).ToArray();
            if (matches.Length == 0 && rule.ExecutablePath is not null && candidates.Any(c => c.IsSelectable &&
                c.Platform == "windows" && c.ExecutablePath is null &&
                string.Equals(c.Executable, Path.GetFileName(rule.ExecutablePath), StringComparison.OrdinalIgnoreCase)))
                return new([], new(WindowErrorCode.PermissionDenied, "发现同名进程，但无法读取完整路径；已禁止按文件名退化匹配"));
            return new(matches);
        }
        catch (RegexMatchTimeoutException)
        {
            return new([], new(WindowErrorCode.RegexTimeout, "窗口匹配正则执行超时"));
        }
    }

    private static bool MatchesIdentity(TargetWindowRule rule, WindowCandidate candidate)
    {
        if (rule.ExecutablePath is not null)
            return candidate.Platform == "windows" && candidate.ExecutablePath is not null &&
                string.Equals(Path.GetFullPath(candidate.ExecutablePath), Path.GetFullPath(rule.ExecutablePath), StringComparison.OrdinalIgnoreCase);
        if (rule.BundleId is not null)
            return candidate.Platform == "macOS" &&
                string.Equals(candidate.BundleId, rule.BundleId, StringComparison.Ordinal);
        return string.Equals(candidate.Executable, rule.Exe, StringComparison.OrdinalIgnoreCase);
    }

    private static Regex? CreateRegex(string? pattern) => string.IsNullOrWhiteSpace(pattern) ? null :
        new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}
