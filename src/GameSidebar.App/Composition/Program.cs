using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using GameSidebar.App.Development;
using GameSidebar.App.Storage;
using GameSidebar.App.ViewModels;
using GameSidebar.App.Views;
using GameSidebar.Application.Abstractions;
using GameSidebar.Application.Sessions;
using GameSidebar.Application.Capture;
using GameSidebar.Infrastructure.Maa;
using GameSidebar.Platform.MacOS;
using GameSidebar.Platform.Windows.Discovery;
using GameSidebar.Platform.Windows.Geometry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;

namespace GameSidebar.App;

internal static class Program
{
    private static IHost? _host;
    [STAThread]
    public static void Main(string[] args)
    {
        var demo = args.Contains("--demo", StringComparer.OrdinalIgnoreCase);
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        var settings = new SettingsStore();
        var targetConfig = new TargetConfigStore(args);
        builder.Logging.AddProvider(new RollingFileLoggerProvider(Path.Combine(settings.DirectoryPath, "logs")));
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(targetConfig);
        builder.Services.AddSingleton<IProfileRepository>(_ => new JsonProfileRepository(Path.Combine(AppContext.BaseDirectory, "profiles")));
        if (demo)
        {
            builder.Services.AddSingleton<FakeWindowService>();
            builder.Services.AddSingleton<IWindowCatalog>(sp => sp.GetRequiredService<FakeWindowService>());
            builder.Services.AddSingleton<IWindowGeometryProvider>(sp => sp.GetRequiredService<FakeWindowService>());
        }
        else if (OperatingSystem.IsWindows()) RegisterWindows(builder.Services);
        else if (OperatingSystem.IsMacOS())
        {
            builder.Services.AddSingleton<MacWindowService>();
            builder.Services.AddSingleton<IWindowCatalog>(sp => sp.GetRequiredService<MacWindowService>());
            builder.Services.AddSingleton<IWindowGeometryProvider>(sp => sp.GetRequiredService<MacWindowService>());
        }
        else throw new PlatformNotSupportedException("真实窗口模式仅支持 Windows/macOS");
        builder.Services.AddSingleton<GameSessionManager>();
        if (demo) builder.Services.AddSingleton<IGameCaptureBackend, FakeCaptureBackend>();
        else builder.Services.AddSingleton<IGameCaptureBackend, MaaCaptureBackend>();
        builder.Services.AddSingleton<CaptureCoordinator>();
        builder.Services.AddSingleton<SidebarViewModel>();
        builder.Services.AddSingleton(sp => new MainViewModel(sp.GetRequiredService<GameSessionManager>(),
            sp.GetRequiredService<SettingsStore>(), sp.GetRequiredService<TargetConfigStore>(),
            demo ? sp.GetRequiredService<FakeWindowService>() : null, demo));
        _host = builder.Build();
        _host.Start();
        _host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup").LogInformation(
            "GameSidebar {Version}; OS {OS}; architecture {Architecture}; mode {Mode}",
            typeof(Program).Assembly.GetName().Version, Environment.OSVersion,
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture, demo ? "Demo" : OperatingSystem.IsMacOS() ? "macOS" : "Windows");
        try { AppBuilder.Configure<SidebarApplication>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args); }
        finally { _host.Dispose(); _host = null; }
    }
    [SupportedOSPlatform("windows")]
    private static void RegisterWindows(IServiceCollection services)
    {
        services.AddSingleton<IWindowCatalog, WindowsWindowCatalog>();
        services.AddSingleton<IWindowGeometryProvider, WindowsWindowGeometryProvider>();
    }
    internal static IHost Host => _host ?? throw new InvalidOperationException("Host 未初始化");
}

public sealed class SidebarApplication : Avalonia.Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = Program.Host.Services.GetRequiredService<MainViewModel>();
            var window = new MainWindow(viewModel);
            var sidebar = new SidebarWindow(Program.Host.Services.GetRequiredService<SidebarViewModel>(),
                Program.Host.Services.GetRequiredService<GameSessionManager>());
            window.Opened += (_, _) => sidebar.Attach(window);
            window.Opened += (_, _) => Program.Host.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Startup").LogInformation("Main window opened in {Mode} mode", viewModel.ModeText);
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Task? shutdownTask = null;
            bool shutdownComplete = false;
            async Task ShutdownAsync()
            {
                await Program.Host.Services.GetRequiredService<CaptureCoordinator>().DisposeAsync();
                sidebar.ViewModel.Dispose();
                sidebar.CloseForShutdown();
                viewModel.Dispose();
                await Program.Host.Services.GetRequiredService<GameSessionManager>().DisposeAsync();
                await Program.Host.StopAsync();
                shutdownComplete = true;
                desktop.Shutdown();
            }
            window.Closing += (_, e) =>
            {
                if (shutdownComplete) return;
                e.Cancel = true;
                shutdownTask ??= ShutdownAsync();
            };
            desktop.ShutdownRequested += (_, e) =>
            {
                if (shutdownComplete) return;
                e.Cancel = true;
                shutdownTask ??= ShutdownAsync();
            };
            desktop.MainWindow = window;
            _ = viewModel.InitializeAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
