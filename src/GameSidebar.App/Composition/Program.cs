using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using GameSidebar.App.Development;
using GameSidebar.App.Storage;
using GameSidebar.App.ViewModels;
using GameSidebar.App.Views;
using GameSidebar.App.Recognition;
using GameSidebar.Application.Abstractions;
using GameSidebar.Application.Sessions;
using GameSidebar.Application.Capture;
using GameSidebar.Application.Recognition;
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
#if ARKSIDE_PRODUCTION
        var demo = false;
#else
        var demo = args.Contains("--demo", StringComparer.OrdinalIgnoreCase);
#endif
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
#if ARKSIDE_PRODUCTION
        var settings = new SettingsStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArkSide"));
        var targetConfig = new TargetConfigStore([], settings.DirectoryPath);
#else
        var settings = new SettingsStore();
        var targetConfig = new TargetConfigStore(args);
#endif
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
        builder.Services.AddSingleton<IOperatorRecognizer, OfflineOperatorRecognizer>();
        builder.Services.AddSingleton(_ => new OperatorCatalogStore());
        builder.Services.AddTransient<OperatorImportViewModel>();
#if !ARKSIDE_PRODUCTION
        builder.Services.AddTransient<OperatorImportWindow>();
#endif
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
    public override void Initialize()
    {
        Name = "ArkSide";
        Styles.Add(new FluentTheme());
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
#if !ARKSIDE_PRODUCTION
            if (desktop.Args?.Contains("--operator-picker", StringComparer.OrdinalIgnoreCase) == true)
            {
                var picker = Program.Host.Services.GetRequiredService<OperatorImportWindow>();
                desktop.MainWindow = picker;
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                var index = Array.IndexOf(desktop.Args, "--screenshot");
                if (index >= 0 && index + 1 < desktop.Args.Length)
                    picker.Opened += async (_, _) => await picker.ImportFromPathAsync(desktop.Args[index + 1]);
                base.OnFrameworkInitializationCompleted();
                return;
            }
#endif
            var viewModel = Program.Host.Services.GetRequiredService<MainViewModel>();
            var window = new MainWindow(viewModel);
#if !ARKSIDE_PRODUCTION
            void OpenSettings() { window.Show(); window.Activate(); }
#endif
            var sidebar = new SidebarWindow(Program.Host.Services.GetRequiredService<SidebarViewModel>(),
                Program.Host.Services.GetRequiredService<GameSessionManager>(), viewModel.SearchAgainAsync);
            sidebar.Opened += async (_, _) =>
            {
                await viewModel.InitializeAsync();
#if !ARKSIDE_PRODUCTION
                if (desktop.Args?.Contains("--diagnostics", StringComparer.OrdinalIgnoreCase) == true) OpenSettings();
#endif
            };
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Task? shutdownTask = null;
            bool shutdownComplete = false;
            async Task ShutdownAsync()
            {
                await viewModel.InitializeAsync();
                await sidebar.ViewModel.StopRecognitionAsync();
                await Program.Host.Services.GetRequiredService<CaptureCoordinator>().DisposeAsync();
                sidebar.ViewModel.Dispose();
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
                window.Hide();
            };
            sidebar.Closing += (_, e) =>
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
            desktop.MainWindow = sidebar;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
