using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GameSidebar.Core.Geometry;
using GameSidebar.Core.Sessions;

namespace GameSidebar.Core.Profiles;

public enum ProfilePurpose { DiagnosticOnly, Game }
public sealed record SidebarDimensions(double ExpandedWidthDip, double CollapsedWidthDip);
public sealed record GameWindowProfile(
    int SchemaVersion, string Id, ProfilePurpose Purpose, string? GameId,
    ClientPixelSize ClientSize, SidebarDimensions Sidebar,
    Dictionary<string, object>? Coordinates = null, Dictionary<string, object>? Regions = null);
public sealed record TargetWindowRule(
    int SchemaVersion = 1, string? GameId = null, string? DisplayName = null,
    string? Exe = null, string? TitleRule = null, string? ClassRule = null,
    string? PreferredProfileId = "diagnostic-1920x1080",
    string? ExecutablePath = null, string? BundleId = null, string? AppBundlePath = null)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ExecutablePath) ||
        !string.IsNullOrWhiteSpace(BundleId) || !string.IsNullOrWhiteSpace(Exe);
}
public enum ProfileValidationKind { Matched, UnsupportedResolution, GeometryUnavailable, AmbiguousProfile, InvalidProfile }
public sealed record ProfileValidationResult(ProfileValidationKind Kind, string Reason, string? ProfileId = null);

public static class ProfileRules
{
    public static WindowOperationError? Validate(TargetWindowRule rule)
    {
        if (rule.SchemaVersion != 1) return new(WindowErrorCode.InvalidConfiguration, "目标规则 schemaVersion 不受支持");
        if (!rule.IsConfigured && (!string.IsNullOrWhiteSpace(rule.TitleRule) || !string.IsNullOrWhiteSpace(rule.ClassRule)))
            return new(WindowErrorCode.InvalidConfiguration, "自动规则需要目标应用");
        if (rule.ExecutablePath is not null && (!Path.IsPathFullyQualified(rule.ExecutablePath) ||
            !Path.IsPathRooted(rule.ExecutablePath) || !File.Exists(rule.ExecutablePath)))
            return new(WindowErrorCode.InvalidConfiguration, $"目标 exe 完整路径无效或文件不存在：{rule.ExecutablePath}");
        foreach (var value in new[] { rule.TitleRule, rule.ClassRule })
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            try { _ = new Regex(value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
            catch (ArgumentException e) { return new(WindowErrorCode.InvalidConfiguration, $"正则表达式无效：{e.Message}"); }
        }
        return null;
    }

    public static WindowOperationError? ValidateProfiles(IReadOnlyList<GameWindowProfile> profiles)
    {
        if (profiles.Count == 0) return new(WindowErrorCode.InvalidConfiguration, "没有可用 Profile");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            if (profile.SchemaVersion != 1 || string.IsNullOrWhiteSpace(profile.Id) || !ids.Add(profile.Id) ||
                !Enum.IsDefined(profile.Purpose) || !profile.ClientSize.IsValid || profile.Sidebar is null ||
                !double.IsFinite(profile.Sidebar.ExpandedWidthDip) || !double.IsFinite(profile.Sidebar.CollapsedWidthDip) ||
                profile.Sidebar.ExpandedWidthDip <= 0 || profile.Sidebar.CollapsedWidthDip <= 0 ||
                profile.Sidebar.CollapsedWidthDip > profile.Sidebar.ExpandedWidthDip ||
                (profile.Purpose == ProfilePurpose.Game && string.IsNullOrWhiteSpace(profile.GameId)))
                return new(WindowErrorCode.InvalidConfiguration, $"Profile 配置无效或 ID 重复：{profile.Id}");
        }
        return null;
    }

    public static ProfileValidationResult Match(IReadOnlyList<GameWindowProfile> profiles, WindowGeometrySnapshot? geometry, string? preference)
    {
        var invalid = ValidateProfiles(profiles);
        if (invalid is not null) return new(ProfileValidationKind.InvalidProfile, invalid.Message);
        if (geometry is null || !geometry.IsUsable) return new(ProfileValidationKind.GeometryUnavailable, "Client 几何暂不可用");
        var matches = profiles.Where(p => p.ClientSize == geometry.ClientSizePx).ToArray();
        if (matches.Length == 0) return new(ProfileValidationKind.UnsupportedResolution, $"不支持的 Client 尺寸：{geometry.ClientSizePx.Width}×{geometry.ClientSizePx.Height}");
        var preferred = matches.FirstOrDefault(p => p.Id.Equals(preference, StringComparison.OrdinalIgnoreCase));
        if (preferred is not null) return new(ProfileValidationKind.Matched, "Client 尺寸匹配；目标游戏兼容性未验证", preferred.Id);
        if (matches.Length > 1) return new(ProfileValidationKind.AmbiguousProfile, "多个 Profile 匹配，请明确选择");
        return new(ProfileValidationKind.Matched, "Client 尺寸匹配；目标游戏兼容性未验证", matches[0].Id);
    }
}
