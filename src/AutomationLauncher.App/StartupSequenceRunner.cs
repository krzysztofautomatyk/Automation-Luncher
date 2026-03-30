using System.Diagnostics;
using System.IO;

namespace AutomationLauncher.App;

public sealed class StartupSequenceRunner : IStartupSequenceRunner
{
    public async Task<StartupSequenceRunResult> RunAsync(
        IReadOnlyList<StartupSequenceEntry> entries,
        StartupSequenceSplashWindow splashWindow,
        CancellationToken cancellationToken)
    {
        var startedCount = 0;
        var failedCount = 0;

        for (var index = 0; index < entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = entries[index];
            var executablePath = entry.ExecutablePath?.Trim();
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                continue;
            }

            var displayName = Path.GetFileName(executablePath);
            var delaySeconds = Math.Max(0, entry.DelaySeconds);
            for (var remaining = delaySeconds; remaining > 0; remaining--)
            {
                splashWindow.SetStatus($"Waiting {remaining}s before launching {displayName} ({index + 1}/{entries.Count})");
                await Task.Delay(1000, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            splashWindow.SetStatus($"Launching {displayName} ({index + 1}/{entries.Count})");

            if (!File.Exists(executablePath))
            {
                splashWindow.SetStatus($"Skipped missing file: {displayName}");
                await Task.Delay(1200, cancellationToken);
                continue;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = executablePath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory
                });

                startedCount++;
                await Task.Delay(800, cancellationToken);
            }
            catch
            {
                failedCount++;
                splashWindow.SetStatus($"Failed to launch {displayName}");
                await Task.Delay(1200, cancellationToken);
            }
        }

        return new StartupSequenceRunResult
        {
            StartedCount = startedCount,
            FailedCount = failedCount,
            Message = BuildResultMessage(startedCount, failedCount)
        };
    }

    private static string BuildResultMessage(int startedCount, int failedCount)
    {
        if (startedCount == 0 && failedCount == 0)
        {
            return "Startup sequence finished with no applications launched.";
        }

        if (failedCount == 0)
        {
            return $"Startup sequence finished. Started {startedCount} application(s).";
        }

        return $"Startup sequence finished. Started {startedCount} application(s), failed {failedCount}.";
    }
}