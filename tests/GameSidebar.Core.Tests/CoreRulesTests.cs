using GameSidebar.Core.Geometry;
using GameSidebar.Core.Profiles;
using GameSidebar.Core.Sessions;

namespace GameSidebar.Core.Tests;

public sealed class CoreRulesTests
{
    private static WindowGeometrySnapshot Geometry(int width = 1920, int height = 1080) => new(
        new(-300, 10, width - 284, height + 49), new(-292, 18, width - 292, height + 41),
        VisibleFrameSource.Dwm, new(width, height), new(-292, 41),
        new(-292, 41, width - 292, height + 41), new(-1920, 0, 0, 1080),
        new(-1920, 0, 0, 1040), "left", 120, DpiAwarenessKind.PerMonitorAwareV2,
        DpiAwarenessKind.PerMonitorAwareV2, GeometryValidity.Valid, DateTimeOffset.UtcNow);
    private static GameWindowProfile Profile(string id, int width = 1920) =>
        new(1, id, ProfilePurpose.DiagnosticOnly, null, new(width, 1080), new(360, 48));

    [Fact] public void Geometry_keeps_negative_screen_coordinates_and_exclusive_edges()
    {
        var geometry = Geometry();
        Assert.Equal(-292, geometry.ClientBoundsScreenPx.Left);
        Assert.Equal(1920, geometry.ClientBoundsScreenPx.Width);
        Assert.Equal(1080, geometry.ClientBoundsScreenPx.Height);
        Assert.True(geometry.IsUsable);
        Assert.False(new ClientPixelSize(0, 1080).IsValid);
    }
    [Fact] public void Profile_match_distinguishes_unsupported_and_unavailable()
    {
        var profiles = new[] { Profile("diagnostic") };
        Assert.Equal(ProfileValidationKind.Matched, ProfileRules.Match(profiles, Geometry(), null).Kind);
        Assert.Equal(ProfileValidationKind.UnsupportedResolution, ProfileRules.Match(profiles, Geometry(1280), null).Kind);
        Assert.Equal(ProfileValidationKind.GeometryUnavailable, ProfileRules.Match(profiles, null, null).Kind);
    }
    [Fact] public void Profile_ambiguity_requires_preference()
    {
        var profiles = new[] { Profile("a"), Profile("b") };
        Assert.Equal(ProfileValidationKind.AmbiguousProfile, ProfileRules.Match(profiles, Geometry(), null).Kind);
        Assert.Equal("b", ProfileRules.Match(profiles, Geometry(), "b").ProfileId);
    }
    [Fact] public void Invalid_profiles_and_rules_are_configuration_errors()
    {
        Assert.NotNull(ProfileRules.ValidateProfiles([Profile("a"), Profile("a")]));
        Assert.NotNull(ProfileRules.ValidateProfiles([new(2, "new", ProfilePurpose.DiagnosticOnly, null, new(1920, 1080), new(360, 48))]));
        Assert.NotNull(ProfileRules.ValidateProfiles([new(1, "game", ProfilePurpose.Game, null, new(1920, 1080), new(360, 48))]));
        Assert.NotNull(ProfileRules.Validate(new TargetWindowRule(Exe: "game.exe", TitleRule: "[")));
        Assert.False(new TargetWindowRule().IsConfigured);
    }
}
