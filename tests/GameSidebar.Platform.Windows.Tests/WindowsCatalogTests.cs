using GameSidebar.Platform.Windows.Discovery;

namespace GameSidebar.Platform.Windows.Tests;

public sealed class WindowsCatalogTests
{
    [WindowsFact]
    public async Task EnumWindows_reports_api_failure_separately_from_empty_candidates()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = await new WindowsWindowCatalog().EnumerateAsync(CancellationToken.None);
        Assert.Null(result.Error);
        Assert.NotNull(result.Candidates);
    }
}

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "需要 Windows 平台";
    }
}
