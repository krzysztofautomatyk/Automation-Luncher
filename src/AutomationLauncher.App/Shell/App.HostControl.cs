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

        var commandBinding = GetConfiguredControlCommandBindings()
            .FirstOrDefault(binding => File.Exists(GetControlFilePath(binding.ControlFileType)));
        if (commandBinding is null)
        {
            return;
        }

        var controlFilePath = GetControlFilePath(commandBinding.ControlFileType);
        Log.Logger.Information(
            "Detected configured control command. Action={Action} Type={ControlFileType} Path={ControlFilePath}",
            commandBinding.Action,
            commandBinding.ControlFileType,
            controlFilePath);

        _isHandlingControlSignal = true;
        try
        {
            DeleteControlFile(controlFilePath);
            CleanupControlFilesExceptRun();
            RefreshErrorMarkerState();
            await HandleConfiguredControlCommandDetectedAsync(commandBinding);
        }
        finally
        {
            _isHandlingControlSignal = false;
        }
    }

    private async Task HandleConfiguredControlCommandDetectedAsync(ControlFileScriptBinding binding)
    {
        switch (binding.Action)
        {
            case HostControlCommandAction.Start:
                await HandleStartControlCommandDetectedAsync(binding);
                return;
            case HostControlCommandAction.Stop:
                await HandleStopControlCommandDetectedAsync(binding);
                return;
            case HostControlCommandAction.Archive:
                await HandleArchiveControlCommandDetectedAsync(binding);
                return;
            default:
                await MarkErrorControlFileAsync($"Control command '.{binding.ControlFileType}' has no valid action configured.");
                _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", $"Control command '.{binding.ControlFileType}' has no valid action configured.", ToolTipIcon.Error);
                return;
        }
    }

    private ControlFileScriptBinding GetPreferredControlCommandBinding(HostControlCommandAction action, string fallbackControlFileType)
    {
        return GetConfiguredControlCommandBindings()
            .FirstOrDefault(binding => binding.Action == action)
            ?? new ControlFileScriptBinding
            {
                Action = action,
                ControlFileType = fallbackControlFileType
            };
    }

    private async Task HandleStartControlCommandDetectedAsync(ControlFileScriptBinding binding)
    {
        var commandLabel = binding.EffectiveDisplayName;
        try
        {
            if (_host is null)
                return;

            var settings = _host.Services.GetRequiredService<AutomationLauncherSettings>();
            if (!await TryRunControlFilePhaseAsync(
                    settings,
                    binding.ControlFileType,
                    isPreExecution: true,
                    $"{commandLabel} pre-execution sequence aborted the control flow."))
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
                    : $"{commandLabel} detected, but startup automation is already running.";
                _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", startMsg, ToolTipIcon.Info);
                return;
            }

            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", $"{commandLabel} detected. Running startup automation.", ToolTipIcon.Info);
            await RunStartupSequenceAsync(settings, "Preparing startup automation from control file...", binding);
            await TryRunControlFilePhaseAsync(
                settings,
                binding.ControlFileType,
                isPreExecution: false,
                $"{commandLabel} post-execution sequence aborted further control flow.");
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Start control command handling failed. Type={ControlFileType}", binding.ControlFileType);
            await MarkErrorControlFileAsync($"{commandLabel} handling failed: {ex.Message}");
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", $"{commandLabel} handling failed. Error marker created.", ToolTipIcon.Error);
        }
    }

    private async Task HandleStopControlCommandDetectedAsync(ControlFileScriptBinding binding)
    {
        var commandLabel = binding.EffectiveDisplayName;
        try
        {
            var settings = _host?.Services.GetService<AutomationLauncherSettings>();
            if (settings is not null
                && !await TryRunControlFilePhaseAsync(
                    settings,
                    binding.ControlFileType,
                    isPreExecution: true,
                    $"{commandLabel} pre-execution sequence aborted the control flow."))
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
                Log.Logger.Information("Stop control command ignored because the launcher is not in the running state. Type={ControlFileType}", binding.ControlFileType);
                _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", $"{commandLabel} ignored because no managed runtime is currently active.", ToolTipIcon.Info);
                return;
            }

            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", $"{commandLabel} detected. Waiting 60 seconds before stopping startup applications.", ToolTipIcon.Info);
            SetTrayIndicatorMode(TrayIndicatorMode.StopPending);
            TransitionHostControlState(HostControlState.Stopping, $"{commandLabel} accepted.");

            if (!await ConfirmStopSequenceAsync(binding))
            {
                TransitionHostControlState(HostControlState.Running, $"{commandLabel} cancelled by user.");
                SetTrayIndicatorMode(_isStartupSequenceRunning ? TrayIndicatorMode.Startup : TrayIndicatorMode.None);
                _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", $"{commandLabel} was cancelled. Startup applications continue running.", ToolTipIcon.Info);
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
                await TryRunControlFilePhaseAsync(
                    settings,
                    binding.ControlFileType,
                    isPreExecution: false,
                    $"{commandLabel} post-execution sequence aborted further control flow.");
            }
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Stop control command handling failed. Type={ControlFileType}", binding.ControlFileType);
            await MarkErrorControlFileAsync($"{commandLabel} handling failed: {ex.Message}");
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", $"{commandLabel} handling failed. Error marker created.", ToolTipIcon.Error);
        }
    }

    private async Task HandleArchiveControlCommandDetectedAsync(ControlFileScriptBinding binding)
    {
        var commandLabel = binding.EffectiveDisplayName;
        try
        {
            if (_host is null)
                return;

            var settings = _host.Services.GetRequiredService<AutomationLauncherSettings>();
            if (!await TryRunControlFilePhaseAsync(
                    settings,
                    binding.ControlFileType,
                    isPreExecution: true,
                    $"{commandLabel} pre-execution sequence aborted the control flow."))
            {
                SetTrayIndicatorMode(GetPreferredTrayIndicatorMode());
                return;
            }

            SetTrayIndicatorMode(TrayIndicatorMode.Archiving);

            if (_host.Services.GetRequiredService<MainWindowViewModel>() is not MainWindowViewModel viewModel)
                return;

            var archiveGuard = _host.Services.GetRequiredService<IHostControlGuard>();
            var archiveReadiness = archiveGuard.CheckArchive(viewModel.IsBusy);
            if (archiveReadiness != HostControlCommandReadiness.Ready)
            {
                Log.Logger.Information("Archive control command ignored because the launcher is already busy. Type={ControlFileType}", binding.ControlFileType);
                _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", $"{commandLabel} ignored because another operation is already running.", ToolTipIcon.Info);
                return;
            }

            DeleteControlFile(GetControlFilePath("archok"));
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", $"{commandLabel} detected. Starting archive workflow.", ToolTipIcon.Info);

            var result = await RunArchiveWithCountdownAsync(viewModel, settings, binding);

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
                    await TryRunControlFilePhaseAsync(
                        settings,
                        binding.ControlFileType,
                        isPreExecution: false,
                        $"{commandLabel} post-execution sequence aborted further control flow.");
                    return;
            }

            await MarkErrorControlFileAsync($"{commandLabel} finished without archive success.");
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", $"{commandLabel} failed. Error marker created.", ToolTipIcon.Error);
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Archive control command handling failed. Type={ControlFileType}", binding.ControlFileType);
            await MarkErrorControlFileAsync($"{commandLabel} handling failed: {ex.Message}");
            _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", $"{commandLabel} handling failed. Error marker created.", ToolTipIcon.Error);
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
