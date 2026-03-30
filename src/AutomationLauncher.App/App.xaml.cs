using System.IO;
using System.Windows;
using System.Threading;
using AutomationLauncher.Domain.Models;
using AutomationLauncher.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace AutomationLauncher.App;

public partial class App : System.Windows.Application
{
    private static Mutex? _singleInstanceMutex;
    private readonly IHost _host;

    public App()
    {
        var configuration = BuildConfiguration();
        var settings = configuration.Get<AutomationLauncherSettings>() ?? new AutomationLauncherSettings();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.WithProperty("Application", "AutomationLauncher")
            .Enrich.WithProperty("MachineName", Environment.MachineName)
            .CreateLogger();

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(services =>
            {
                services.AddSingleton(configuration);
                services.AddSingleton(settings);
                services.AddInfrastructure(settings.Archive, Log.Logger);
                services.AddSingleton<IRuntimeSelectionSettingsStore>(_ => new JsonRuntimeSelectionSettingsStore(GetUserSettingsFilePath()));
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Logger.Error(args.Exception, "Unhandled UI exception");
            MessageBox.Show($"Critical error: {args.Exception.Message}", "AutomationLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Logger.Fatal(args.ExceptionObject as Exception, "Unhandled domain exception");
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, "Global\\AutomationLauncher.Singleton", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("AutomationLauncher is already running.", "AutomationLauncher", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        await _host.StartAsync();
        var window = _host.Services.GetRequiredService<MainWindow>();
        window.Show();
        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile(GetUserSettingsFilePath(), optional: true, reloadOnChange: true)
            .Build();
    }

    private static string GetUserSettingsFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutomationLauncher",
            "user-settings.json");
    }
}
