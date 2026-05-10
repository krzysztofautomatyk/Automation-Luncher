using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AutomationLauncher.App;

public partial class App : System.Windows.Application
{
    private void TrackStartupProcess(Process process)
    {
        lock (_startupProcessesSyncRoot)
        {
            _startupLaunchedProcesses.RemoveAll(startedProcess => startedProcess.HasExited);
            _startupLaunchedProcesses.Add(process);
        }
    }

    private async Task StopTrackedStartupProcessesAsync()
    {
        List<Process> trackedProcesses;
        lock (_startupProcessesSyncRoot)
        {
            trackedProcesses = _startupLaunchedProcesses
                .GroupBy(process => process.Id)
                .Select(group => group.Last())
                .ToList();
            _startupLaunchedProcesses.Clear();
        }

        foreach (var process in trackedProcesses)
        {
            try
            {
                if (process.HasExited)
                    continue;

                var exitedGracefully = false;
                try
                {
                    if (process.CloseMainWindow())
                    {
                        exitedGracefully = await WaitForProcessExitAsync(process, 5000);
                    }
                }
                catch (InvalidOperationException)
                {
                    exitedGracefully = true;
                }

                if (!exitedGracefully && !process.HasExited)
                {
                    process.Kill();
                    await WaitForProcessExitAsync(process, 3000);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "Failed to stop startup-launched process {ProcessId}", process.Id);
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private void DisposeTrackedStartupProcesses()
    {
        lock (_startupProcessesSyncRoot)
        {
            foreach (var process in _startupLaunchedProcesses)
            {
                process.Dispose();
            }

            _startupLaunchedProcesses.Clear();
        }
    }

    private async Task<bool> ConfirmStopSequenceAsync(ControlFileScriptBinding? controlBinding = null)
    {
        if (_host is null)
        {
            return true;
        }

        var settings = _host.Services.GetRequiredService<AutomationLauncherSettings>();
        var splashWindow = _host.Services.GetRequiredService<StartupSequenceSplashWindow>();
        var requestedImmediateStop = false;
        var requestedKeepRunning = false;
        var isCancellationDialogOpen = false;

        void HandleCancellationDialogOpened(object? sender, System.EventArgs e) => isCancellationDialogOpen = true;
        void HandleCancellationDialogClosed(object? sender, System.EventArgs e) => isCancellationDialogOpen = false;
        void HandleCancelRequested(object? sender, StartupSplashCancelRequestedEventArgs e)
        {
            requestedKeepRunning = true;
            Log.Logger.Information("Stop sequence request was cancelled by user. Reason: {CancellationReason}", e.Reason);
        }
        void HandleConfirmRequested(object? sender, System.EventArgs e) => requestedImmediateStop = true;

        ApplyReactionSplashSettings(splashWindow, settings, controlBinding, HostControlCommandAction.Stop, "Automation Launcher");
        splashWindow.ConfigureActions(showConfirmAction: true, confirmButtonText: "Stop now", cancelButtonText: "Keep running");
        splashWindow.ConfigureConfirmDialog("Stop startup applications now without waiting for the countdown?");
        splashWindow.ConfigureCancelDialog(
            "Keep startup applications running",
            "Provide a reason why startup applications should keep running:");
        splashWindow.CancelRequested += HandleCancelRequested;
        splashWindow.ConfirmRequested += HandleConfirmRequested;
        splashWindow.CancellationDialogOpened += HandleCancellationDialogOpened;
        splashWindow.CancellationDialogClosed += HandleCancellationDialogClosed;
        splashWindow.Show();

        try
        {
            var countdownSeconds = GetReactionCountdownSeconds(controlBinding, HostControlCommandAction.Stop, 60);
            for (var remainingSeconds = countdownSeconds; remainingSeconds > 0; remainingSeconds--)
            {
                splashWindow.SetStatus($"Stop requested. Startup applications will be stopped in {remainingSeconds}s. Click Cancel to keep them running.");
                var elapsedMilliseconds = 0;

                while (elapsedMilliseconds < 1000)
                {
                    if (requestedImmediateStop)
                    {
                        splashWindow.SetStatus("Stopping startup applications immediately...");
                        await Task.Delay(250);
                        return true;
                    }

                    if (requestedKeepRunning)
                        return false;

                    await Task.Delay(100);

                    if (isCancellationDialogOpen)
                        continue;

                    elapsedMilliseconds += 100;
                }
            }

            splashWindow.SetStatus("Stopping startup applications...");
            await Task.Delay(500);
            return true;
        }
        finally
        {
            splashWindow.CancelRequested -= HandleCancelRequested;
            splashWindow.ConfirmRequested -= HandleConfirmRequested;
            splashWindow.CancellationDialogOpened -= HandleCancellationDialogOpened;
            splashWindow.CancellationDialogClosed -= HandleCancellationDialogClosed;
            splashWindow.Close();
        }
    }

    private async Task WaitForStartupSequenceToStopAsync()
    {
        var attemptsRemaining = 25;
        while (_isStartupSequenceRunning && attemptsRemaining-- > 0)
        {
            await Task.Delay(200);
        }
    }

    private static Task<bool> WaitForProcessExitAsync(Process process, int timeoutMilliseconds)
    {
        return Task.Run(() =>
        {
            try
            {
                return process.WaitForExit(timeoutMilliseconds);
            }
            catch
            {
                return true;
            }
        });
    }
}
