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
        var fallbackDirectory = AppContext.BaseDirectory ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fallbackDirectory))
        {
            fallbackDirectory = Environment.CurrentDirectory ?? ".";
        }

        if (string.IsNullOrWhiteSpace(fallbackDirectory))
        {
            fallbackDirectory = ".";
        }

        var configuredDirectory = _host?.Services.GetService<AutomationLauncherSettings>()?.Ui?.ControlFilesDirectory;
        var directory = fallbackDirectory;
        var trimmedConfiguredDirectory = configuredDirectory?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedConfiguredDirectory))
        {
            directory = trimmedConfiguredDirectory!;
        }

        try
        {
            Directory.CreateDirectory(directory);
            return directory;
        }
        catch
        {
            return fallbackDirectory;
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
        foreach (var binding in GetConfiguredControlCommandBindings())
        {
            DeleteControlFile(GetControlFilePath(binding.ControlFileType));
        }
    }

    private async Task EnsureRunControlFileExistsAsync()
    {
        var runFilePath = GetControlFilePath("run");
        if (!File.Exists(runFilePath))
        {
            await WriteControlFileWithAutomationAsync("run");
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

    private async Task MarkErrorControlFileAsync(string reason)
    {
        Log.Logger.Error("Control-flow error marker requested. Reason: {Reason}", reason);
        CleanupControlFilesExceptRun();
        await WriteControlFileWithAutomationAsync("error");
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
        var emittedControlTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var controlFileType in GetFixedControlFileTypes())
        {
            if (emittedControlTypes.Add(controlFileType))
            {
                yield return GetControlFilePath(controlFileType);
            }
        }

        foreach (var binding in GetConfiguredControlCommandBindings())
        {
            if (emittedControlTypes.Add(binding.ControlFileType))
            {
                yield return GetControlFilePath(binding.ControlFileType);
            }
        }
    }

    private IEnumerable<ControlFileScriptBinding> GetConfiguredControlCommandBindings(AutomationLauncherSettings? settings = null)
    {
        settings ??= _host?.Services.GetService<AutomationLauncherSettings>();
        foreach (var binding in settings?.ControlFiles?.Bindings ?? Enumerable.Empty<ControlFileScriptBinding>())
        {
            if (binding is null
                || !ControlFileScriptBinding.TryNormalizeControlFileType(binding.ControlFileType, out var normalizedType)
                || ControlFileScriptBinding.IsReservedMarkerType(normalizedType))
            {
                continue;
            }

            yield return binding;
        }
    }

    private static IEnumerable<string> GetFixedControlFileTypes()
    {
        yield return "run";
        yield return "ready";
        yield return "error";
        yield return "archok";
    }

    private async Task WriteControlFileWithAutomationAsync(string controlFileType, AutomationLauncherSettings? settings = null)
    {
        settings ??= _host?.Services.GetService<AutomationLauncherSettings>();
        var path = GetControlFilePath(controlFileType);

        var orchestrator = _host?.Services.GetService<AutomationLauncher.App.Services.IControlFileScriptOrchestrator>();
        if (orchestrator is null)
        {
            WriteControlFile(path);
            return;
        }

        var directory = GetControlFilesRootDirectory();
        var preResult = await orchestrator.ExecuteAsync(controlFileType, true, _hostControlState.ToString(), directory);
        if (!preResult.ShouldContinueControlFlow)
        {
            Log.Logger.Warning(
                "Skipped writing control file {ControlFileType} because pre-execution aborted. Details: {Details}",
                controlFileType, preResult.Message);
            return;
        }

        WriteControlFile(path);

        var postResult = await orchestrator.ExecuteAsync(controlFileType, false, _hostControlState.ToString(), directory);
        if (!postResult.ShouldContinueControlFlow)
        {
            Log.Logger.Warning(
                "Post-execution sequence for control file {ControlFileType} aborted further control flow. Details: {Details}",
                controlFileType, postResult.Message);
        }
    }
}
