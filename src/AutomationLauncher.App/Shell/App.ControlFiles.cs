using System.IO;
using System.Linq;
using System.Collections.Generic;
using AutomationLauncher.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AutomationLauncher.App;

public partial class App : System.Windows.Application
{
    private string GetControlFilePath(string state)
    {
        return Path.Combine(GetControlFilesRootDirectory(), $"{System.Environment.MachineName}.{state}");
    }

    private string GetControlFilesRootDirectory()
    {
        var configuredDirectory = _host?.Services.GetService<AutomationLauncherSettings>()?.Ui?.ControlFilesDirectory;
        var directory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? AppContext.BaseDirectory
            : configuredDirectory.Trim();

        try
        {
            Directory.CreateDirectory(directory);
            return directory;
        }
        catch
        {
            return AppContext.BaseDirectory;
        }
    }

    private static void WriteControlFile(string path)
    {
        try
        {
            File.WriteAllText(path, $"{System.Environment.MachineName} {System.DateTimeOffset.Now:O}");
            Log.Logger.Information("Created control file {ControlFileName} at {ControlFilePath}", Path.GetFileName(path), path);
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "Failed to write control file {ControlFilePath}", path);
        }
    }

    private static void DeleteControlFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
            Log.Logger.Information("Deleted control file {ControlFileName} at {ControlFilePath}", Path.GetFileName(path), path);
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "Failed to delete control file {ControlFilePath}", path);
        }
    }

    private void DeleteControlCommandFiles()
    {
        DeleteControlFile(GetControlFilePath("start"));
        DeleteControlFile(GetControlFilePath("stop"));
        DeleteControlFile(GetControlFilePath("march"));
    }

    private void EnsureRunControlFileExists()
    {
        var runFilePath = GetControlFilePath("run");
        if (!File.Exists(runFilePath))
        {
            WriteControlFile(runFilePath);
        }
    }

    private void CleanupControlFilesExceptRun()
    {
        foreach (var controlFilePath in GetManagedControlFilePaths().Where(path => !path.EndsWith(".run", StringComparison.OrdinalIgnoreCase)))
        {
            DeleteControlFile(controlFilePath);
        }
    }

    private void ClearAllHostControlFiles()
    {
        foreach (var controlFilePath in GetManagedControlFilePaths())
        {
            DeleteControlFile(controlFilePath);
        }
    }

    private void MarkErrorControlFile(string reason)
    {
        Log.Logger.Error("Control-flow error marker requested. Reason: {Reason}", reason);
        CleanupControlFilesExceptRun();
        WriteControlFile(GetControlFilePath("error"));
        RefreshErrorMarkerState();
        TransitionHostControlState(HostControlState.Error, reason);
        SetTrayIndicatorMode(TrayIndicatorMode.Error);
    }

    private void DeleteErrorMarkerFile()
    {
        var errorFilePath = GetControlFilePath("error");
        if (!File.Exists(errorFilePath))
        {
            RefreshErrorMarkerState();
            return;
        }

        DeleteControlFile(errorFilePath);
        RefreshErrorMarkerState();
        _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", "Error marker file deleted.", System.Windows.Forms.ToolTipIcon.Info);
    }

    private void RefreshErrorMarkerState()
    {
        var hasErrorControlFile = File.Exists(GetControlFilePath("error"));
        if (_hasErrorControlFile == hasErrorControlFile)
        {
            if (_host?.Services.GetService<MainWindowViewModel>() is MainWindowViewModel currentViewModel)
            {
                currentViewModel.SetErrorControlFilePresent(_hasErrorControlFile);
            }

            return;
        }

        _hasErrorControlFile = hasErrorControlFile;

        if (_host?.Services.GetService<MainWindowViewModel>() is MainWindowViewModel viewModel)
        {
            viewModel.SetErrorControlFilePresent(_hasErrorControlFile);
        }

        if (_hasErrorControlFile)
        {
            if (_hostControlState != HostControlState.Error)
            {
                TransitionHostControlState(HostControlState.Error, "Error marker file exists.");
            }

            SetTrayIndicatorMode(TrayIndicatorMode.Error);
            UpdateTrayMenuState();
            return;
        }

        if (_hostControlState == HostControlState.Error)
        {
            TransitionHostControlState(HostControlState.Ready, "Error marker file removed.");
        }

        SetTrayIndicatorMode(GetPreferredTrayIndicatorMode());
        UpdateTrayMenuState();
    }

    private void NormalizeHostControlState()
    {
        var normalizedState = _hasErrorControlFile
            ? HostControlState.Error
            : HostControlState.Ready;
        TransitionHostControlState(normalizedState, "Application startup normalization.");
    }

    private IEnumerable<string> GetManagedControlFilePaths()
    {
        yield return GetControlFilePath("run");
        yield return GetControlFilePath("ready");
        yield return GetControlFilePath("error");
        yield return GetControlFilePath("start");
        yield return GetControlFilePath("stop");
        yield return GetControlFilePath("march");
        yield return GetControlFilePath("archok");
    }
}
