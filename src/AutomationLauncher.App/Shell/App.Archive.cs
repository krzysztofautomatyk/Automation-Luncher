using System;
using System.Threading;
using System.Threading.Tasks;
using AutomationLauncher.Domain.Contracts;
using AutomationLauncher.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AutomationLauncher.App;

public partial class App : System.Windows.Application
{
    /// <summary>
    /// Full archive flow with splash:
    /// 1. Announce archive — 60s countdown (cancel / start now)
    /// 2. Online state check — PLC must be online
    /// 3. Compare online vs offline 1:1
    /// 4. Go offline
    /// 5. Save wait — 60s for user, then auto-save
    /// 6. Archive
    /// Returns true = success, false = failed, null = cancelled.
    /// </summary>
    private async Task<bool?> RunArchiveWithCountdownAsync(MainWindowViewModel viewModel, AutomationLauncherSettings settings, ControlFileScriptBinding? controlBinding = null)
    {
        var splashWindow = _host!.Services.GetRequiredService<StartupSequenceSplashWindow>();
        var archiveNowRequested = false;
        var archiveCancelled = false;
        var isCancelDialogOpen = false;
        var saveNowRequested = false;
        var skipSaveRequested = false;

        void HandleConfirm(object? s, EventArgs e) => archiveNowRequested = true;
        void HandleCancel(object? s, StartupSplashCancelRequestedEventArgs e)
        {
            archiveCancelled = true;
            Log.Logger.Information("Archive countdown cancelled by user. Reason: {Reason}", e.Reason);
        }
        void HandleDialogOpened(object? s, EventArgs e) => isCancelDialogOpen = true;
        void HandleDialogClosed(object? s, EventArgs e) => isCancelDialogOpen = false;
        void HandleSaveNow(object? s, EventArgs e) => saveNowRequested = true;
        void HandleSkipSave(object? s, EventArgs e) => skipSaveRequested = true;

        ApplyReactionSplashSettings(splashWindow, settings, controlBinding, HostControlCommandAction.Archive, "Automation Launcher");
        splashWindow.ConfigureActions(showConfirmAction: true, confirmButtonText: "Archive now", cancelButtonText: "Cancel archive");
        splashWindow.ConfigureConfirmDialog("Start archive immediately without waiting for the countdown?");
        splashWindow.ConfigureCancelDialog("Cancel archive", "Provide a reason for cancelling the archive:");
        splashWindow.ConfigureSaveAction(visible: false, saveText: null, skipText: null);

        splashWindow.ConfirmRequested += HandleConfirm;
        splashWindow.CancelRequested += HandleCancel;
        splashWindow.CancellationDialogOpened += HandleDialogOpened;
        splashWindow.CancellationDialogClosed += HandleDialogClosed;
        splashWindow.SaveNowRequested += HandleSaveNow;
        splashWindow.SkipSaveRequested += HandleSkipSave;
        splashWindow.Show();

        try
        {
            // ── Phase 1: 60-second countdown — user can cancel or start now ──
            Log.Logger.Information("Archive process starting. 60 seconds to cancel or start now.");
            var countdownSeconds = GetReactionCountdownSeconds(controlBinding, HostControlCommandAction.Archive, 60);
            for (var remaining = countdownSeconds; remaining > 0; remaining--)
            {
                splashWindow.SetStatus($"Archive process starting in {remaining}s. Click 'Archive now' to proceed or 'Cancel' to abort.");
                var elapsed = 0;
                while (elapsed < 1000)
                {
                    if (archiveNowRequested || archiveCancelled)
                        break;
                    await Task.Delay(100);
                    if (!isCancelDialogOpen)
                        elapsed += 100;
                }
                if (archiveNowRequested || archiveCancelled)
                    break;
            }

            if (archiveCancelled)
            {
                splashWindow.SetStatus("Archive cancelled by user.");
                await Task.Delay(600);
                return null;
            }

            // Hide confirm/cancel buttons — from here on the workflow is automatic
            splashWindow.ConfigureActions(showConfirmAction: false, confirmButtonText: null, cancelButtonText: null, showCancelAction: false);

            // ── Phase 2–4: Use case handles online check → compare → go offline → save → archive ──
            // The use case will save automatically. But if project has unsaved changes after go-offline,
            // we show a 60-second save countdown before auto-save.
            var gateway = _host.Services.GetRequiredService<ITiaPortalGateway>();
            var sessionId = string.Empty;

            // Quick context read to get session ID for pre-save UI
            splashWindow.SetStatus("Connecting to TIA Portal...");
            var preContext = await gateway.GetCurrentContextAsync(CancellationToken.None);
            sessionId = preContext.SessionId ?? string.Empty;

            // Check if we need a save wait after go-offline
            // The use case will do the actual online/compare/go-offline steps
            // We provide save info via PendingPreSave
            var preSaveAttempted = false;
            var preSaveSucceeded = (bool?)null;
            var preSaveSource = (string?)null;

            if (preContext.IsTiaRunning && !string.IsNullOrWhiteSpace(sessionId))
            {
                var needsSave = false;
                if (preContext.UnsavedStateDetectedReliably && preContext.HasUnsavedChanges == true)
                    needsSave = true;
                else if (!preContext.UnsavedStateDetectedReliably && settings.Archive.ForceSaveWhenDetectionUnavailable)
                    needsSave = true;

                if (needsSave)
                {
                    // Show save countdown — 60 seconds for user to save, then auto-save
                    splashWindow.ConfigureSaveAction(visible: true, saveText: "Save now", skipText: "Skip save");
                    splashWindow.SetSaveStatus("\u26a0 Unsaved changes detected \u2014 auto-save in 60s");

                    var (attempted, succeeded, source) = await RunSaveCountdownAsync(
                        splashWindow, gateway, sessionId, settings,
                        "Unsaved changes detected",
                        saveWaitSeconds: 60,
                        getSaveNow: () => saveNowRequested,
                        getSkipSave: () => skipSaveRequested,
                        getArchiveCancelled: () => false, // can't cancel at this phase
                        CancellationToken.None);

                    preSaveAttempted = attempted;
                    preSaveSucceeded = succeeded;
                    preSaveSource = source;
                }
            }

            splashWindow.ConfigureSaveAction(visible: false, saveText: null, skipText: null);
            splashWindow.SetSaveStatus(null);
            splashWindow.SetStatus("Running archive workflow: online check \u2192 compare \u2192 go offline \u2192 save \u2192 archive...");

            viewModel.PendingPreSave = (preSaveAttempted, preSaveSucceeded, preSaveSource);
            var archiveCreated = await viewModel.RunArchiveFromControlFileWithResultAsync();

            splashWindow.SetStatus(archiveCreated
                ? "\u2713 Archive completed successfully."
                : "\u26a0 Archive workflow finished. Check operation history for details.");
            await Task.Delay(1200);

            return archiveCreated;
        }
        finally
        {
            splashWindow.ConfirmRequested -= HandleConfirm;
            splashWindow.CancelRequested -= HandleCancel;
            splashWindow.CancellationDialogOpened -= HandleDialogOpened;
            splashWindow.CancellationDialogClosed -= HandleDialogClosed;
            splashWindow.SaveNowRequested -= HandleSaveNow;
            splashWindow.SkipSaveRequested -= HandleSkipSave;
            splashWindow.Close();
        }
    }

    /// <summary>
    /// Counts down then auto-saves. User can click "Save now" or "Skip save".
    /// Returns (attempted, succeeded, triggerSource).
    /// </summary>
    private static async Task<(bool Attempted, bool? Succeeded, string? TriggerSource)> RunSaveCountdownAsync(
        StartupSequenceSplashWindow splash,
        ITiaPortalGateway gateway,
        string sessionId,
        AutomationLauncherSettings settings,
        string baseMessage,
        int saveWaitSeconds,
        Func<bool> getSaveNow,
        Func<bool> getSkipSave,
        Func<bool> getArchiveCancelled,
        CancellationToken cancellationToken)
    {
        for (var remaining = saveWaitSeconds; remaining > 0; remaining--)
        {
            if (getArchiveCancelled() || getSkipSave() || cancellationToken.IsCancellationRequested)
            {
                Log.Logger.Information("Archive pre-save skipped by user or archive cancelled.");
                splash.SetSaveStatus(null);
                splash.ConfigureSaveAction(visible: false, saveText: null, skipText: null);
                return (false, null, null);
            }

            if (getSaveNow())
                break;

            splash.SetSaveStatus($"\u26a0 {baseMessage} \u2014 auto-save in {remaining}s");

            var elapsed = 0;
            while (elapsed < 1000)
            {
                if (getArchiveCancelled() || getSkipSave() || getSaveNow() || cancellationToken.IsCancellationRequested)
                    break;
                await Task.Delay(100);
                elapsed += 100;
            }
        }

        // Re-check after loop exits
        if (getArchiveCancelled() || getSkipSave() || cancellationToken.IsCancellationRequested)
        {
            Log.Logger.Information("Archive pre-save skipped by user or archive cancelled.");
            splash.SetSaveStatus(null);
            splash.ConfigureSaveAction(visible: false, saveText: null, skipText: null);
            return (false, null, null);
        }

        var triggerSource = getSaveNow() ? "UserSaveNow" : "AutoSaveCountdown";
        Log.Logger.Information("Archive pre-save starting. TriggerSource={TriggerSource} SessionId={SessionId}", triggerSource, sessionId);

        splash.SetSaveStatus("Saving project...");
        splash.ConfigureSaveAction(visible: false, saveText: null, skipText: null);

        var saveOk = await gateway.SaveProjectAsync(
            sessionId,
            TimeSpan.FromSeconds(settings.Archive.SaveTimeoutSeconds),
            CancellationToken.None);

        Log.Logger.Information("Archive pre-save completed. TriggerSource={TriggerSource} SessionId={SessionId} Success={Success}", triggerSource, sessionId, saveOk);

        splash.SetSaveStatus(saveOk
            ? "\u2713 Project saved successfully."
            : "\u26a0 Save failed \u2014 archive will attempt to save again.");

        if (saveOk)
            await Task.Delay(1500);

        return (true, saveOk, triggerSource);
    }
}
