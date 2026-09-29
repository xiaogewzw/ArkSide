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
            return new(candidates.Where(c => c.IsAutoEligible &&
                string.Equals(c.Executable, rule.Exe, StringComparison.OrdinalIgnoreCase) &&
                (title is null || title.IsMatch(c.Title)) &&
                (className is null || className.IsMatch(c.ClassName))).ToArray());
        }
        catch (RegexMatchTimeoutException)
        {
            return new([], new(WindowErrorCode.RegexTimeout, "窗口匹配正则执行超时"));
        }
    }

    private static Regex? CreateRegex(string? pattern) => string.IsNullOrWhiteSpace(pattern) ? null :
        new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}
