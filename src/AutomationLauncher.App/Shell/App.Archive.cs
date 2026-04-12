using System;
using System.Threading.Tasks;
using AutomationLauncher.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AutomationLauncher.App;

public partial class App : System.Windows.Application
{
    /// <summary>
    /// Shows a 60-second countdown splash window before running the archive workflow.
    /// Returns true = success, false = archive ran but failed, null = user cancelled.
    /// </summary>
    private async Task<bool?> RunArchiveWithCountdownAsync(MainWindowViewModel viewModel, AutomationLauncherSettings settings)
    {
        var splashWindow = _host!.Services.GetRequiredService<StartupSequenceSplashWindow>();
        var archiveNowRequested = false;
        var archiveCancelled = false;
        var isCancelDialogOpen = false;

        void HandleConfirm(object? s, EventArgs e) => archiveNowRequested = true;
        void HandleCancel(object? s, StartupSplashCancelRequestedEventArgs e)
        {
            archiveCancelled = true;
            Log.Logger.Information("Archive countdown cancelled by user. Reason: {Reason}", e.Reason);
        }
        void HandleDialogOpened(object? s, EventArgs e) => isCancelDialogOpen = true;
        void HandleDialogClosed(object? s, EventArgs e) => isCancelDialogOpen = false;

        splashWindow.SetApplicationTitle("Automation Launcher");
        splashWindow.SetBackgroundImage(settings.Startup.SplashBackgroundImagePath);
        splashWindow.ConfigureActions(showConfirmAction: true, confirmButtonText: "Archive now", cancelButtonText: "Cancel archive");
        splashWindow.ConfigureConfirmDialog("Start archive immediately without waiting for the countdown?");
        splashWindow.ConfigureCancelDialog("Cancel archive", "Provide a reason for cancelling the archive:");

        splashWindow.ConfirmRequested += HandleConfirm;
        splashWindow.CancelRequested += HandleCancel;
        splashWindow.CancellationDialogOpened += HandleDialogOpened;
        splashWindow.CancellationDialogClosed += HandleDialogClosed;
        splashWindow.Show();

        try
        {
            for (var remainingSeconds = 60; remainingSeconds > 0; remainingSeconds--)
            {
                splashWindow.SetStatus($"Archive starts in {remainingSeconds}s. Click Archive now to skip the countdown.");
                var elapsedMilliseconds = 0;

                while (elapsedMilliseconds < 1000)
                {
                    if (archiveNowRequested || archiveCancelled)
                        break;

                    await Task.Delay(100);

                    if (isCancelDialogOpen)
                        continue;

                    elapsedMilliseconds += 100;
                }

                if (archiveNowRequested || archiveCancelled)
                    break;
            }

            if (archiveCancelled)
            {
                splashWindow.SetStatus("Archive cancelled.");
                await Task.Delay(600);
                return null;
            }

            splashWindow.ConfigureActions(showConfirmAction: false, confirmButtonText: null, cancelButtonText: null, showCancelAction: false);
            splashWindow.SetStatus("Running archive workflow...");

            var archiveCreated = await viewModel.RunArchiveFromControlFileWithResultAsync();

            splashWindow.SetStatus(archiveCreated
                ? "Archive completed successfully."
                : "Archive workflow finished. Check operation history for details.");
            await Task.Delay(1200);

            return archiveCreated;
        }
        finally
        {
            splashWindow.ConfirmRequested -= HandleConfirm;
            splashWindow.CancelRequested -= HandleCancel;
            splashWindow.CancellationDialogOpened -= HandleDialogOpened;
            splashWindow.CancellationDialogClosed -= HandleDialogClosed;
            splashWindow.Close();
        }
    }
}
