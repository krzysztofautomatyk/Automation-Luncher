using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using AutomationLauncher.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AutomationLauncher.App;

public partial class App : System.Windows.Application
{
    private async Task RunStartupSequenceIfRequiredAsync(string[] startupArgs, AutomationLauncherSettings settings)
    {
        if (!_launchedFromWindowsStartup || !settings.Startup.RunOnWindowsStartup || !settings.Startup.RunSequenceOnWindowsStartup)
        {
            return;
        }

        await RunStartupSequenceAsync(settings, "Preparing startup automation...");
    }

    private async Task RunStartupSequenceManuallyAsync()
    {
        if (_host is null || _sessionCoordinator?.IsAuthenticated != true)
        {
            return;
        }

        var settings = _host.Services.GetRequiredService<AutomationLauncherSettings>();
        await RunStartupSequenceAsync(settings, "Preparing manual startup automation...");
    }

    private async Task RunStartupSequenceAsync(AutomationLauncherSettings settings, string initialStatus)
    {
        if (_host is null)
        {
            return;
        }

        if (_isStartupSequenceRunning)
        {
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Startup automation is already running.", ToolTipIcon.Info);
            return;
        }

        if (_hostControlState == HostControlState.Running || _hostControlState == HostControlState.Stopping)
        {
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Managed applications are already active. Stop them before starting a new sequence.", ToolTipIcon.Info);
            return;
        }

        var entries = settings.Startup.SequenceEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ExecutablePath))
            .Select(entry => entry.Clone())
            .ToList();

        if (entries.Count == 0)
        {
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "No startup automation items are configured.", ToolTipIcon.Warning);
            return;
        }

        var splashWindow = _host.Services.GetRequiredService<StartupSequenceSplashWindow>();
        var runner = _host.Services.GetRequiredService<IStartupSequenceRunner>();
        var viewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
        var completedSuccessfully = false;
        _startupSequenceCancellationSource = new CancellationTokenSource();
        _isStartupSequenceRunning = true;
        viewModel.SetStartupAutomationRunning(true);
        TransitionHostControlState(HostControlState.Running, "Startup automation sequence started.");
        StartStartupIndicator();
        UpdateTrayMenuState();

        splashWindow.SetApplicationTitle("Automation Launcher");
        splashWindow.SetBackgroundImage(settings.Startup.SplashBackgroundImagePath);
        splashWindow.ConfigureActions(showConfirmAction: true, confirmButtonText: "Start now", cancelButtonText: "Cancel startup");
        splashWindow.ConfigureConfirmDialog("Start startup automation immediately?");
        splashWindow.ConfigureCancelDialog(
            "Cancel startup automation",
            "Provide a reason for cancelling the startup automation:");
        var startImmediatelyRequested = false;
        var isStartupCancellationDialogOpen = false;

        void HandleStartupCancellationDialogOpened(object? sender, System.EventArgs e) => isStartupCancellationDialogOpen = true;
        void HandleStartupCancellationDialogClosed(object? sender, System.EventArgs e) => isStartupCancellationDialogOpen = false;
        void HandleStartupSplashConfirmRequested(object? sender, System.EventArgs e) => startImmediatelyRequested = true;

        splashWindow.CancelRequested += HandleStartupSplashCancelRequested;
        splashWindow.ConfirmRequested += HandleStartupSplashConfirmRequested;
        splashWindow.CancellationDialogOpened += HandleStartupCancellationDialogOpened;
        splashWindow.CancellationDialogClosed += HandleStartupCancellationDialogClosed;
        splashWindow.Show();

        try
        {
            for (var remainingSeconds = 10; remainingSeconds > 0; remainingSeconds--)
            {
                splashWindow.SetStatus($"Startup automation begins in {remainingSeconds}s. Click Start now to run immediately.");
                var elapsedMilliseconds = 0;

                while (elapsedMilliseconds < 1000)
                {
                    if (startImmediatelyRequested)
                        break;

                    if (_startupSequenceCancellationSource.Token.IsCancellationRequested)
                        throw new OperationCanceledException(_startupSequenceCancellationSource.Token);

                    await Task.Delay(100);

                    if (isStartupCancellationDialogOpen)
                        continue;

                    elapsedMilliseconds += 100;
                }

                if (startImmediatelyRequested)
                    break;
            }

            if (_startupSequenceCancellationSource.Token.IsCancellationRequested)
                throw new OperationCanceledException(_startupSequenceCancellationSource.Token);

            splashWindow.ConfigureActions(showConfirmAction: false, confirmButtonText: null, cancelButtonText: "Cancel startup");
            splashWindow.SetStatus(initialStatus);

            var result = await runner.RunAsync(
                entries,
                splashWindow,
                _startupSequenceCancellationSource.Token,
                TrackStartupProcess);
            completedSuccessfully = true;
            splashWindow.SetStatus(result.Message);
            await Task.Delay(900);

            _notifyIcon?.ShowBalloonTip(3000, "Automation Launcher", result.Message, ToolTipIcon.Info);
        }
        catch (OperationCanceledException)
        {
            await StopTrackedStartupProcessesAsync();
            TransitionHostControlState(HostControlState.Ready, "Startup automation was cancelled.");
            _notifyIcon?.ShowBalloonTip(3000, "Automation Launcher", "Startup automation was cancelled.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            await StopTrackedStartupProcessesAsync();
            Log.Logger.Error(ex, "Startup automation failed");
            MarkErrorControlFile($"Startup automation failed: {ex.Message}");
            _notifyIcon?.ShowBalloonTip(3000, "Automation Launcher", $"Startup automation failed: {ex.Message}", ToolTipIcon.Error);
        }
        finally
        {
            splashWindow.CancelRequested -= HandleStartupSplashCancelRequested;
            splashWindow.ConfirmRequested -= HandleStartupSplashConfirmRequested;
            splashWindow.CancellationDialogOpened -= HandleStartupCancellationDialogOpened;
            splashWindow.CancellationDialogClosed -= HandleStartupCancellationDialogClosed;
            splashWindow.Close();
            _startupSequenceCancellationSource?.Dispose();
            _startupSequenceCancellationSource = null;
            _isStartupSequenceRunning = false;
            viewModel.SetStartupAutomationRunning(false);
            StopStartupIndicator();

            if (completedSuccessfully && _hostControlState != HostControlState.Running)
                TransitionHostControlState(HostControlState.Running, "Startup automation completed successfully.");

            UpdateTrayMenuState();
        }
    }

    private void HandleStartupSplashCancelRequested(object? sender, StartupSplashCancelRequestedEventArgs e)
    {
        Log.Logger.Warning("Startup automation cancellation requested by user. Reason: {CancellationReason}", e.Reason);
        _startupSequenceCancellationSource?.Cancel();
    }
}
