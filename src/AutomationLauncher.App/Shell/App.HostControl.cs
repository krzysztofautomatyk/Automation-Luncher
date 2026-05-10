using System.IO;
using System.Windows.Forms;
using AutomationLauncher.Domain.Contracts;
using AutomationLauncher.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AutomationLauncher.App;

public partial class App : System.Windows.Application
{
    private async Task InitializeHostControlFlowAsync()
    {
        DeleteControlCommandFiles();
        await EnsureRunControlFileExistsAsync();
        RefreshErrorMarkerState();
        NormalizeHostControlState();
        StartStartupControlFileMonitor();
    }

    private void StartStartupControlFileMonitor()
    {
        _startupControlFileTimer ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = System.TimeSpan.FromSeconds(3)
        };

        _startupControlFileTimer.Tick -= HandleStartupControlFileTimerTick;
        _startupControlFileTimer.Tick += HandleStartupControlFileTimerTick;
        _startupControlFileTimer.Start();
    }

    private async void HandleStartupControlFileTimerTick(object? sender, System.EventArgs e)
    {
        if (_isHandlingControlSignal)
            return;

        await EnsureRunControlFileExistsAsync();
        RefreshErrorMarkerState();

        var startFilePath = GetControlFilePath("start");
        if (File.Exists(startFilePath))
        {
            Log.Logger.Information("Detected start control file at {ControlFilePath}", startFilePath);
            _isHandlingControlSignal = true;
            try
            {
                DeleteControlFile(startFilePath);
                CleanupControlFilesExceptRun();
                RefreshErrorMarkerState();
                await HandleStartControlFileDetectedAsync();
            }
            finally { _isHandlingControlSignal = false; }
            return;
        }

        var stopFilePath = GetControlFilePath("stop");
        if (File.Exists(stopFilePath))
        {
            Log.Logger.Information("Detected stop control file at {ControlFilePath}", stopFilePath);
            _isHandlingControlSignal = true;
            try
            {
                DeleteControlFile(stopFilePath);
                CleanupControlFilesExceptRun();
                RefreshErrorMarkerState();
                await HandleStopControlFileDetectedAsync();
            }
            finally { _isHandlingControlSignal = false; }
            return;
        }

        var marchFilePath = GetControlFilePath("march");
        if (!File.Exists(marchFilePath))
            return;

        Log.Logger.Information("Detected march control file at {ControlFilePath}", marchFilePath);
        _isHandlingControlSignal = true;
        try
        {
            DeleteControlFile(marchFilePath);
            CleanupControlFilesExceptRun();
            RefreshErrorMarkerState();
            await HandleMarchControlFileDetectedAsync();
        }
        finally { _isHandlingControlSignal = false; }
    }

    private async Task HandleStartControlFileDetectedAsync()
    {
        try
        {
            if (_host is null)
                return;

            var settings = _host.Services.GetRequiredService<AutomationLauncherSettings>();
            if (!await TryRunControlFilePhaseAsync(settings, "start", isPreExecution: true, "start command pre-execution sequence aborted the control flow."))
            {
                SetTrayIndicatorMode(GetPreferredTrayIndicatorMode());
                return;
            }

            SetTrayIndicatorMode(TrayIndicatorMode.Startup);

            var startGuard = _host.Services.GetRequiredService<IHostControlGuard>();
            var startEntryCount = settings.Startup.SequenceEntries.Count(e => !string.IsNullOrWhiteSpace(e.ExecutablePath));
            var startReadiness = startGuard.CheckStart(_hostControlState, _isStartupSequenceRunning, startEntryCount);
            if (startReadiness != HostControlCommandReadiness.Ready)
            {
                var startMsg = startReadiness == HostControlCommandReadiness.NoEntriesConfigured
                    ? "No startup automation items are configured."
                    : "Start command detected, but startup automation is already running.";
                _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", startMsg, ToolTipIcon.Info);
                return;
            }

            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Start command detected. Running startup automation.", ToolTipIcon.Info);
            await RunStartupSequenceAsync(settings, "Preparing startup automation from control file...");
            await TryRunControlFilePhaseAsync(settings, "start", isPreExecution: false, "start command post-execution sequence aborted further control flow.");
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Start command handling failed");
            await MarkErrorControlFileAsync($"Start command handling failed: {ex.Message}");
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Start command handling failed. Error marker created.", ToolTipIcon.Error);
        }
    }

    private async Task HandleStopControlFileDetectedAsync()
    {
        try
        {
            var settings = _host?.Services.GetService<AutomationLauncherSettings>();
            if (settings is not null && !await TryRunControlFilePhaseAsync(settings, "stop", isPreExecution: true, "stop command pre-execution sequence aborted the control flow."))
            {
                SetTrayIndicatorMode(GetPreferredTrayIndicatorMode());
                return;
            }

            SetTrayIndicatorMode(TrayIndicatorMode.StopPending);

            var stopGuard = _host?.Services.GetService<IHostControlGuard>();
            var stopReadiness = stopGuard?.CheckStop(_hostControlState, _isStartupSequenceRunning)
                ?? HostControlCommandReadiness.Ready;
            if (stopReadiness != HostControlCommandReadiness.Ready)
            {
                Log.Logger.Information("Stop command ignored because the launcher is not in the running state.");
                _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Stop command ignored because no managed runtime is currently active.", ToolTipIcon.Info);
                return;
            }

            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Stop command detected. Waiting 60 seconds before stopping startup applications.", ToolTipIcon.Info);
            SetTrayIndicatorMode(TrayIndicatorMode.StopPending);
            TransitionHostControlState(HostControlState.Stopping, "Stop command accepted.");

            if (!await ConfirmStopSequenceAsync())
            {
                TransitionHostControlState(HostControlState.Running, "Stop command cancelled by user.");
                SetTrayIndicatorMode(_isStartupSequenceRunning ? TrayIndicatorMode.Startup : TrayIndicatorMode.None);
                _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Stop command was cancelled. Startup applications continue running.", ToolTipIcon.Info);
                return;
            }

            if (_isStartupSequenceRunning)
            {
                _startupSequenceCancellationSource?.Cancel();
                await WaitForStartupSequenceToStopAsync();
            }

            await StopTrackedStartupProcessesAsync();
            TransitionHostControlState(HostControlState.Ready, "Managed applications were stopped.");
            await WriteControlFileWithAutomationAsync("ready", settings);
            SetTrayIndicatorMode(TrayIndicatorMode.None);
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Startup applications were stopped. Ready marker created.", ToolTipIcon.Info);
            if (settings is not null)
            {
                await TryRunControlFilePhaseAsync(settings, "stop", isPreExecution: false, "stop command post-execution sequence aborted further control flow.");
            }
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Stop command handling failed");
            await MarkErrorControlFileAsync($"Stop command handling failed: {ex.Message}");
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Stop command handling failed. Error marker created.", ToolTipIcon.Error);
        }
    }

    private async Task HandleMarchControlFileDetectedAsync()
    {
        try
        {
            if (_host is null)
                return;

            var settings = _host.Services.GetRequiredService<AutomationLauncherSettings>();
            if (!await TryRunControlFilePhaseAsync(settings, "march", isPreExecution: true, "march command pre-execution sequence aborted the control flow."))
            {
                SetTrayIndicatorMode(GetPreferredTrayIndicatorMode());
                return;
            }

            SetTrayIndicatorMode(TrayIndicatorMode.Archiving);

            if (_host.Services.GetRequiredService<MainWindowViewModel>() is not MainWindowViewModel viewModel)
                return;

            var marchGuard = _host.Services.GetRequiredService<IHostControlGuard>();
            var marchReadiness = marchGuard.CheckMarch(viewModel.IsBusy);
            if (marchReadiness != HostControlCommandReadiness.Ready)
            {
                Log.Logger.Information("March command ignored because the launcher is already busy.");
                _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Archive command ignored because another operation is already running.", ToolTipIcon.Info);
                return;
            }

            DeleteControlFile(GetControlFilePath("archok"));
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "March command detected. Starting archive workflow.", ToolTipIcon.Info);

            var result = await RunArchiveWithCountdownAsync(viewModel, settings);

            switch (result)
            {
                case null:
                    SetTrayIndicatorMode(GetPreferredTrayIndicatorMode());
                    _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Archive cancelled by user.", ToolTipIcon.Info);
                    return;
                case true:
                    await WriteControlFileWithAutomationAsync("archok", settings);
                    SetTrayIndicatorMode(GetPreferredTrayIndicatorMode());
                    _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Archive created successfully. Archok marker file written.", ToolTipIcon.Info);
                    await TryRunControlFilePhaseAsync(settings, "march", isPreExecution: false, "march command post-execution sequence aborted further control flow.");
                    return;
            }

            await MarkErrorControlFileAsync("March command finished without archive success.");
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "March command failed. Error marker created.", ToolTipIcon.Error);
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "March command handling failed");
            await MarkErrorControlFileAsync($"March command handling failed: {ex.Message}");
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "March command handling failed. Error marker created.", ToolTipIcon.Error);
        }
    }

    private void TransitionHostControlState(HostControlState newState, string reason)
    {
        var previousState = _hostControlState;
        _hostControlState = newState;
        NotifyHostControlStateChanged(newState);
        Log.Logger.Information("Host control state changed from {PreviousState} to {NewState}. Reason: {Reason}", previousState, newState, reason);
    }

    private void NotifyHostControlStateChanged(HostControlState state)
    {
        if (_host?.Services.GetService<MainWindowViewModel>() is MainWindowViewModel viewModel)
        {
            viewModel.SetHostControlState(state);
        }
    }
}