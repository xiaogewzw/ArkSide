using Avalonia.Controls;

namespace GameSidebar.App.Views;

internal static class AppIcon
{
    public static WindowIcon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("ArkSide.Icon.png")
            ?? throw new InvalidOperationException("应用图标缺失");
        return new WindowIcon(stream);
    }
}
