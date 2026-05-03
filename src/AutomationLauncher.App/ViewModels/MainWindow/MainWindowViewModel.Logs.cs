using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using AutomationLauncher.Application.UseCases;
using AutomationLauncher.Domain.Contracts;
using AutomationLauncher.Domain.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Forms = System.Windows.Forms;
using FileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace AutomationLauncher.App;
public partial class MainWindowViewModel : ObservableObject
{
    private void HandleSessionCountdownTick(object? sender, EventArgs e)
    {
        UpdateSessionCountdown();
    }

    private void HandleFileLogRefreshTick(object? sender, EventArgs e)
    {
        _ = RefreshFileLogsAsync();
    }

    private void HandleFileLogsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(VisibleLogCount));
        OnPropertyChanged(nameof(ErrorLogCount));
        OnPropertyChanged(nameof(WarnLogCount));
    }

    private void UpdateSessionCountdown()
    {
        if (!_sessionCoordinator.IsAuthenticated)
        {
            SessionTimeRemaining = "Locked";
            return;
        }

        var remaining = _sessionCoordinator.GetRemainingInactivity();
        SessionTimeRemaining = $"{Math.Max(0, (int)remaining.TotalMinutes):00}:{remaining.Seconds:00}";
    }

    private async Task RefreshFileLogsAsync(bool forceRefresh = false)
    {
        if (!await _fileLogRefreshSemaphore.WaitAsync(0))
        {
            _pendingFileLogRefresh = true;
            return;
        }

        try
        {
            do
            {
                _pendingFileLogRefresh = false;

                var refreshResult = await Task.Run(() => ReadLatestLogSnapshot(forceRefresh));
                if (refreshResult is null)
                {
                    continue;
                }

                if (refreshResult.ErrorMessage is not null)
                {
                    AddHistory("WARN", "LogReadFailed", refreshResult.ErrorMessage);
                }

                LoadedLogFilePath = refreshResult.LoadedLogFilePath;
                _lastLogSnapshotKey = refreshResult.SnapshotKey;

                _allFileLogLines.Clear();
                _allFileLogLines.AddRange(refreshResult.LogLines);

                await ApplyLogFilterAsync();
                forceRefresh = false;
            }
            while (_pendingFileLogRefresh);
        }
        finally
        {
            _fileLogRefreshSemaphore.Release();
        }
    }

    private async Task ApplyLogFilterAsync()
    {
        var snapshot = _allFileLogLines.ToArray();
        var searchTerm = (LogSearchText ?? string.Empty).Trim();
        var showErrorsAndWarningsOnlySnapshot = ShowErrorsAndWarningsOnly;

        var filteredEntries = await Task.Run(() => BuildFilteredLogEntries(snapshot, searchTerm, showErrorsAndWarningsOnlySnapshot));
        FileLogs.ReplaceRange(filteredEntries);
    }

    private LogRefreshResult? ReadLatestLogSnapshot(bool forceRefresh)
    {
        var logDirectoryPath = ResolveEffectiveLogDirectory();
        if (!Directory.Exists(logDirectoryPath))
        {
            return LogRefreshResult.Empty("No log file loaded.");
        }

        var logFiles = Directory.GetFiles(logDirectoryPath, "automation-launcher-*.log");
        if (logFiles.Length == 0)
        {
            return LogRefreshResult.Empty("No log file loaded.");
        }

        var activeLogFilePath = GetNewestLogFilePath(logFiles);

        var snapshotKey = activeLogFilePath;
        try
        {
            var info = new FileInfo(activeLogFilePath);
            snapshotKey = $"{info.FullName}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            // Keep path-only snapshot key when metadata cannot be read.
        }

        if (!forceRefresh && string.Equals(snapshotKey, _lastLogSnapshotKey, StringComparison.Ordinal))
        {
            return null;
        }

        var logLines = new Queue<string>();
        string? errorMessage = null;
        try
        {
            foreach (var line in ReadSharedLogLines(activeLogFilePath))
            {
                logLines.Enqueue(line);
                while (logLines.Count > MaxDisplayedLogLines)
                {
                    _ = logLines.Dequeue();
                }
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"Unable to read active log file: {ex.Message}";
        }

        return new LogRefreshResult(activeLogFilePath, snapshotKey, logLines.ToArray(), errorMessage);
    }

    private static IReadOnlyList<LogLineEntry> BuildFilteredLogEntries(IReadOnlyList<string> logLines, string searchTerm, bool showErrorsAndWarningsOnly)
    {
        var hasSearchTerm = !string.IsNullOrWhiteSpace(searchTerm);
        var filteredEntries = new List<LogLineEntry>(logLines.Count);

        for (var index = logLines.Count - 1; index >= 0; index--)
        {
            var logLine = logLines[index];
            var level = ExtractLogLevel(logLine);
            if (showErrorsAndWarningsOnly && level is not ("ERR" or "FTL" or "WRN"))
            {
                continue;
            }

            if (hasSearchTerm && logLine.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            filteredEntries.Add(new LogLineEntry(logLine, level, hasSearchTerm));
        }

        return filteredEntries;
    }

    private static string ExtractLogLevel(string logLine)
    {
        var openBracketIndex = logLine.IndexOf('[');
        if (openBracketIndex < 0)
        {
            return "N/A";
        }

        var closeBracketIndex = logLine.IndexOf(']', openBracketIndex + 1);
        if (closeBracketIndex < 0)
        {
            return "N/A";
        }

        var tokenLength = closeBracketIndex - openBracketIndex - 1;
        if (tokenLength != 3)
        {
            return "N/A";
        }

        return logLine.Substring(openBracketIndex + 1, tokenLength).ToUpperInvariant();
    }

    private static string GetNewestLogFilePath(IEnumerable<string> logFiles)
    {
        string? newestPath = null;
        var newestTimestamp = DateTime.MinValue;

        foreach (var logFile in logFiles)
        {
            DateTime candidateTimestamp;
            try
            {
                candidateTimestamp = File.GetLastWriteTimeUtc(logFile);
            }
            catch
            {
                candidateTimestamp = DateTime.MinValue;
            }

            if (newestPath is null || candidateTimestamp > newestTimestamp)
            {
                newestPath = logFile;
                newestTimestamp = candidateTimestamp;
            }
        }

        return newestPath ?? string.Empty;
    }

    private static IEnumerable<string> ReadSharedLogLines(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (line is not null)
            {
                yield return line;
            }
        }
    }

    private string ResolveEffectiveLogDirectory()
    {
        var configuredPath = string.IsNullOrWhiteSpace(LogDirectory)
            ? "logs"
            : LogDirectory;

        var preferredDirectory = LogPathHelper.ResolveDirectory(configuredPath);
        try
        {
            Directory.CreateDirectory(preferredDirectory);
            return preferredDirectory;
        }
        catch
        {
            var fallbackDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AutomationLauncher",
                "logs");
            Directory.CreateDirectory(fallbackDirectory);
            return fallbackDirectory;
        }
    }
}

