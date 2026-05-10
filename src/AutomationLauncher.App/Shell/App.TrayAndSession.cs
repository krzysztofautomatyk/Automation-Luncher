using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using AutomationLauncher.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AutomationLauncher.App;

public partial class App : System.Windows.Application
{
    // ---- ExecuteOnUiThread helpers ----

    private void ExecuteOnUiThread(System.Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.Invoke(action);
    }

    private Task ExecuteOnUiThreadAsync(Func<Task> action)
    {
        if (Dispatcher.CheckAccess())
        {
            return action();
        }

        return Dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    // ---- Session management ----

    private void StartSessionTimer()
    {
        _sessionTimer = new DispatcherTimer
        {
            Interval = System.TimeSpan.FromSeconds(30)
        };
        _sessionTimer.Tick += HandleSessionTimerTick;
        _sessionTimer.Start();
    }

    private void HandleSessionTimerTick(object? sender, System.EventArgs e)
    {
        if (_sessionCoordinator?.HasTimedOut() == true)
        {
            LogoutSession("Session locked after 5 minutes of inactivity.", true);
        }
    }

    private void HandlePreProcessInput(object? sender, PreProcessInputEventArgs e)
    {
        if (_sessionCoordinator is null || !_sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        if (e.StagingItem.Input is System.Windows.Input.MouseEventArgs or System.Windows.Input.KeyboardEventArgs)
        {
            _sessionCoordinator.RegisterActivity();
        }
    }

    private void LogoutSession(string reason, bool isAutomatic)
    {
        _sessionCoordinator?.Logout(reason, isAutomatic);
    }

    private void LoginSession()
    {
        if (_sessionCoordinator is null || _sessionCoordinator.IsAuthenticated)
        {
            return;
        }

        _sessionCoordinator.EnsureAuthenticated(_mainWindow);
        UpdateTrayMenuState();
    }

    private void HandleSessionStateChanged(object? sender, SessionStateChangedEventArgs e)
    {
        if (!e.IsAuthenticated)
        {
            foreach (var settingsWindow in Windows.OfType<Window>().Where(window => window is SettingsWindow).ToList())
            {
                settingsWindow.Close();
            }

            _notifyIcon?.ShowBalloonTip(3000, "Automation Launcher", e.Message, ToolTipIcon.Info);
            UpdateTrayMenuState();
            return;
        }

        _sessionCoordinator?.RegisterActivity();
        UpdateTrayMenuState();
    }

    // ---- Public API bridges (called from dashboard / menu) ----

    public void OpenSettingsFromDashboard()
    {
        ShowSettingsDialog();
    }

    public void OpenAboutFromDashboard()
    {
        ShowAboutDialog();
    }

    public async Task RunStartupAutomationFromDashboardAsync()
    {
        await RunStartupSequenceManuallyAsync();
    }

    public async Task RunManagedApplicationsFromMenuAsync()
    {
        if (_sessionCoordinator?.IsAuthenticated != true)
        {
            return;
        }

        await HandleStartControlCommandDetectedAsync(GetPreferredControlCommandBinding(HostControlCommandAction.Start, "start"));
    }

    public async Task RunArchiveNowFromMenuAsync()
    {
        if (_sessionCoordinator?.IsAuthenticated != true || _host is null)
        {
            return;
        }

        var viewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
        if (viewModel.IsBusy)
        {
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Archive command ignored because another operation is already running.", ToolTipIcon.Info);
            return;
        }

        SetTrayIndicatorMode(TrayIndicatorMode.Archiving);
        DeleteControlFile(GetControlFilePath("archok"));

        var settings = _host.Services.GetRequiredService<AutomationLauncherSettings>();
        var result = await RunArchiveWithCountdownAsync(viewModel, settings);

        switch (result)
        {
            case null:
                SetTrayIndicatorMode(GetPreferredTrayIndicatorMode());
                _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Archive cancelled by user.", ToolTipIcon.Info);
                return;
            case true:
                SetTrayIndicatorMode(GetPreferredTrayIndicatorMode());
                _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Archive created successfully.", ToolTipIcon.Info);
                return;
        }

        await MarkErrorControlFileAsync("Manual archive request failed.");
        _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Archive failed. Error marker created.", ToolTipIcon.Error);
    }

    public async Task RunArchiveNowFromDashboardAsync()
    {
        await RunArchiveNowFromMenuAsync();
    }

    public async Task StopManagedApplicationsFromMenuAsync()
    {
        if (_sessionCoordinator?.IsAuthenticated != true)
        {
            return;
        }

        await HandleStopControlCommandDetectedAsync(GetPreferredControlCommandBinding(HostControlCommandAction.Stop, "stop"));
    }

    public void ExitFromDashboard()
    {
        ExitFromTray();
    }

    public void LoginFromDashboard()
    {
        LoginSession();
    }

    public void DeleteErrorFromDashboard()
    {
        DeleteErrorMarkerFile();
    }
}
