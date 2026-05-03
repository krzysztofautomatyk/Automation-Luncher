using System.Linq;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace AutomationLauncher.App;

public partial class MainWindowViewModel : ObservableObject
{
    private PowerShellScriptExecutionContext BuildManualExecutionContext(ProjectScriptEntry script)
    {
        return new PowerShellScriptExecutionContext
        {
            ScriptName = string.IsNullOrWhiteSpace(script.Name) ? script.Id : script.Name,
            ControlFileType = "manual",
            ExecutionPhase = "manual",
            MachineName = Environment.MachineName,
            HostState = CurrentHostControlState.ToString(),
            AppBaseDirectory = AppContext.BaseDirectory,
            ControlFilesDirectory = ControlFilesFolderPath,
            StartedAtUtc = DateTimeOffset.UtcNow,
            Parameters = script.Parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.DefaultValue ?? string.Empty, StringComparer.OrdinalIgnoreCase)
        };
    }

    private void RefreshProjectScriptPreview()
    {
        if (SelectedProjectScriptEntry is null)
        {
            ProjectScriptPreview = string.Empty;
            return;
        }

        ProjectScriptPreview = _powerShellScriptRunner.PreviewScript(SelectedProjectScriptEntry.ScriptBody, BuildManualExecutionContext(SelectedProjectScriptEntry));
    }

    private void RefreshControlFileStepPreview()
    {
        if (SelectedControlFileScriptBinding is null || ActiveControlFileScriptStep is null)
        {
            ControlFileStepPreviewStatus = "Select a control-file step to edit parameter overrides and preview the final script.";
            ControlFileStepPreview = string.Empty;
            return;
        }

        var script = ProjectScriptEntries.FirstOrDefault(candidate => string.Equals(candidate.Id, ActiveControlFileScriptStep.ScriptId, StringComparison.OrdinalIgnoreCase));
        if (script is null)
        {
            ControlFileStepPreviewStatus = "The selected step does not reference an existing script.";
            ControlFileStepPreview = string.Empty;
            return;
        }

        var executionContext = BuildControlFileExecutionContext(script, ActiveControlFileScriptStep, SelectedControlFileScriptBinding.ControlFileType, ActiveControlFileScriptPhase);
        ControlFileStepPreviewStatus = $"Preview for {SelectedControlFileScriptBinding.DisplayName} ({ActiveControlFileScriptPhase}) using script '{(string.IsNullOrWhiteSpace(script.Name) ? script.Id : script.Name)}'.";
        ControlFileStepPreview = _powerShellScriptRunner.PreviewScript(script.ScriptBody, executionContext);
    }

    private PowerShellScriptExecutionContext BuildControlFileExecutionContext(ProjectScriptEntry script, ControlFileScriptSequenceStep step, string controlFileType, string phase)
    {
        var parameterMap = script.Parameters.ToDictionary(
            parameter => parameter.Name,
            parameter => parameter.DefaultValue ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);

        foreach (var overrideEntry in step.ParameterOverrides.Where(overrideEntry => !string.IsNullOrWhiteSpace(overrideEntry.Name)))
        {
            parameterMap[overrideEntry.Name] = overrideEntry.Value ?? string.Empty;
        }

        return new PowerShellScriptExecutionContext
        {
            ScriptName = string.IsNullOrWhiteSpace(script.Name) ? script.Id : script.Name,
            ControlFileType = controlFileType,
            ExecutionPhase = phase,
            MachineName = Environment.MachineName,
            HostState = CurrentHostControlState.ToString(),
            AppBaseDirectory = AppContext.BaseDirectory,
            ControlFilesDirectory = ControlFilesFolderPath,
            StartedAtUtc = DateTimeOffset.UtcNow,
            Parameters = parameterMap
        };
    }

    [RelayCommand]
    private void AddProjectScriptEntry()
    {
        if (!EnsureAuthenticated())
        {
            return;
        }

        var nextIndex = ProjectScriptEntries.Count + 1;
        var entry = new ProjectScriptEntry
        {
            Name = $"Script {nextIndex}",
            ScriptBody = "Write-Host \"Hello from Automation Launcher\"\nexit 0",
            TimeoutSeconds = 300
        };

        ProjectScriptEntries.Add(entry);
        SelectedProjectScriptEntry = entry;
        PersistSettings("Project script added.");
    }

    [RelayCommand]
    private void SaveProjectScripts()
    {
        if (!EnsureAuthenticated())
        {
            return;
        }

        PersistSettings("Project scripts and control-file automation saved.");
    }

    [RelayCommand]
    private void OpenProjectScriptInstructions()
    {
        var window = new ScriptInstructionsWindow
        {
            Owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
                ?? System.Windows.Application.Current?.MainWindow
        };

        window.ShowDialog();
    }

    [RelayCommand]
    private void RemoveSelectedProjectScriptEntry()
    {
        if (!EnsureAuthenticated() || SelectedProjectScriptEntry is null)
        {
            return;
        }

        var entryToRemove = SelectedProjectScriptEntry;
        ProjectScriptEntries.Remove(entryToRemove);
        SelectedProjectScriptEntry = ProjectScriptEntries.FirstOrDefault();
        PersistSettings("Project script removed.");
    }

    [RelayCommand]
    private void MoveProjectScriptEntryUp()
    {
        if (!EnsureAuthenticated() || SelectedProjectScriptEntry is null)
        {
            return;
        }

        var currentIndex = ProjectScriptEntries.IndexOf(SelectedProjectScriptEntry);
        if (currentIndex <= 0)
        {
            return;
        }

        ProjectScriptEntries.Move(currentIndex, currentIndex - 1);
        PersistSettings("Project script order updated.");
    }

    [RelayCommand]
    private void MoveProjectScriptEntryDown()
    {
        if (!EnsureAuthenticated() || SelectedProjectScriptEntry is null)
        {
            return;
        }

        var currentIndex = ProjectScriptEntries.IndexOf(SelectedProjectScriptEntry);
        if (currentIndex < 0 || currentIndex >= ProjectScriptEntries.Count - 1)
        {
            return;
        }

        ProjectScriptEntries.Move(currentIndex, currentIndex + 1);
        PersistSettings("Project script order updated.");
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedProjectScript))]
    private async Task RunSelectedProjectScriptAsync()
    {
        if (!EnsureAuthenticated() || SelectedProjectScriptEntry is null)
        {
            return;
        }

        var script = SelectedProjectScriptEntry;
        IsRunningProjectScript = true;
        script.IsRunning = true;
        script.LastRunStatus = $"Running {script.Name}...";
        ProjectScriptExecutionStatus = script.LastRunStatus;
        ProjectScriptExecutionOutput = string.Empty;

        try
        {
            var executionContext = BuildManualExecutionContext(script);

            var result = await _powerShellScriptRunner.RunAsync(script.ScriptBody, script.TimeoutSeconds, executionContext, CancellationToken.None);

            script.LastRunFinishedAt = DateTimeOffset.Now;
            script.LastExitCode = result.ExitCode;
            script.LastOutput = result.CombinedOutput;
            script.LastRunStatus = result.IsSuccess
                ? $"Success. Exit code {result.ExitCode}. Finished {script.LastRunFinishedAt:yyyy-MM-dd HH:mm:ss}."
                : $"Failure. Exit code {result.ExitCode}. Finished {script.LastRunFinishedAt:yyyy-MM-dd HH:mm:ss}. {result.StatusMessage}";

            ProjectScriptExecutionStatus = script.LastRunStatus;
            ProjectScriptExecutionOutput = result.CombinedOutput;

            AddHistory(result.IsSuccess ? "OK" : "ERROR",
                result.IsSuccess ? "ProjectScriptSucceeded" : "ProjectScriptFailed",
                $"{script.Name}: {script.LastRunStatus}");
        }
        catch (Exception ex)
        {
            script.LastRunFinishedAt = DateTimeOffset.Now;
            script.LastExitCode = null;
            script.LastOutput = ex.ToString();
            script.LastRunStatus = $"Failure. Runner error: {ex.Message}";
            ProjectScriptExecutionStatus = script.LastRunStatus;
            ProjectScriptExecutionOutput = script.LastOutput;
            AddHistory("ERROR", "ProjectScriptRunnerFailed", $"{script.Name}: {ex.Message}");
        }
        finally
        {
            script.IsRunning = false;
            IsRunningProjectScript = false;
            RunSelectedProjectScriptCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void ExportProjectScriptLibrary()
    {
        if (!EnsureAuthenticated())
        {
            return;
        }

        SyncSettingsModel();

        var dialog = new SaveFileDialog
        {
            Title = "Export script library",
            Filter = "Script library (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            FileName = $"automation-launcher-script-library-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var package = new ScriptLibraryPackage
            {
                Scripts = ProjectScriptEntries.Select(script => script.Clone()).ToList(),
                ControlFileBindings = ControlFileScriptBindings.Select(binding => binding.Clone()).ToList()
            };

            var json = JsonSerializer.Serialize(package, BuildSettingsSerializerOptions());
            File.WriteAllText(dialog.FileName, json);
            SettingsStatusMessage = $"Script library exported to {dialog.FileName}";
            AddHistory("OK", "ScriptLibraryExported", SettingsStatusMessage);
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"Script library export failed: {ex.Message}";
            AddHistory("ERROR", "ScriptLibraryExportFailed", ex.Message);
        }
    }

    [RelayCommand]
    private void ImportProjectScriptLibrary()
    {
        if (!EnsureAuthenticated())
        {
            return;
        }

        var dialog = new FileDialog
        {
            Title = "Import script library",
            Filter = "Script library (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var package = JsonSerializer.Deserialize<ScriptLibraryPackage>(json, BuildSettingsSerializerOptions());
            if (package is null)
            {
                throw new InvalidOperationException("Imported script library is empty or invalid.");
            }

            _settings.Project.PowerShellScripts = package.Scripts?.Select(script => script.Clone()).ToList() ?? new List<ProjectScriptEntry>();
            _settings.ControlFiles.Bindings = package.ControlFileBindings?.Select(binding => binding.Clone()).ToList() ?? ControlFileScriptBinding.CreateDefaultBindings();
            ReloadFromSettings();
            PersistSettings("Script library imported.");
            SettingsStatusMessage = $"Script library imported from {dialog.FileName}";
            AddHistory("OK", "ScriptLibraryImported", SettingsStatusMessage);
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"Script library import failed: {ex.Message}";
            AddHistory("ERROR", "ScriptLibraryImportFailed", ex.Message);
        }
    }

    [RelayCommand]
    private void AddProjectScriptParameter()
    {
        if (!EnsureAuthenticated() || SelectedProjectScriptEntry is null)
        {
            return;
        }

        SelectedProjectScriptEntry.Parameters.Add(new ProjectScriptParameterEntry
        {
            Name = $"Parameter{SelectedProjectScriptEntry.Parameters.Count + 1}",
            DefaultValue = string.Empty
        });
        SelectedProjectScriptParameter = SelectedProjectScriptEntry.Parameters.LastOrDefault();
        PersistSettings("Script parameters updated.");
    }

    [RelayCommand]
    private void RemoveProjectScriptParameter(ProjectScriptParameterEntry? parameter)
    {
        if (!EnsureAuthenticated() || SelectedProjectScriptEntry is null || parameter is null)
        {
            return;
        }

        SelectedProjectScriptEntry.Parameters.Remove(parameter);
        PersistSettings("Script parameters updated.");
    }

    [RelayCommand]
    private void AddPreControlFileScriptStep()
    {
        if (!EnsureAuthenticated() || SelectedControlFileScriptBinding is null)
        {
            return;
        }

        var step = new ControlFileScriptSequenceStep
        {
            ScriptId = ProjectScriptEntries.FirstOrDefault()?.Id ?? string.Empty,
            OnSuccess = ControlFileScriptOutcomeAction.RunNextScript,
            OnFailure = ControlFileScriptOutcomeAction.AbortControlFlow
        };

        SelectedControlFileScriptBinding.PreExecutionSteps.Add(step);
        SelectedPreControlFileScriptStep = step;
        ActiveControlFileScriptStep = step;
        ActiveControlFileScriptPhase = "pre";
        PersistSettings("Pre-execution control-file sequence updated.");
    }

    [RelayCommand]
    private void RemoveSelectedPreControlFileScriptStep()
    {
        if (!EnsureAuthenticated() || SelectedControlFileScriptBinding is null || SelectedPreControlFileScriptStep is null)
        {
            return;
        }

        SelectedControlFileScriptBinding.PreExecutionSteps.Remove(SelectedPreControlFileScriptStep);
        SelectedPreControlFileScriptStep = SelectedControlFileScriptBinding.PreExecutionSteps.FirstOrDefault();
        ActiveControlFileScriptStep = SelectedPreControlFileScriptStep;
        PersistSettings("Pre-execution control-file sequence updated.");
    }

    [RelayCommand]
    private void MovePreControlFileScriptStepUp()
    {
        if (!EnsureAuthenticated() || SelectedControlFileScriptBinding is null || SelectedPreControlFileScriptStep is null)
        {
            return;
        }

        var currentIndex = SelectedControlFileScriptBinding.PreExecutionSteps.IndexOf(SelectedPreControlFileScriptStep);
        if (currentIndex <= 0)
        {
            return;
        }

        SelectedControlFileScriptBinding.PreExecutionSteps.Move(currentIndex, currentIndex - 1);
        PersistSettings("Pre-execution control-file sequence order updated.");
    }

    [RelayCommand]
    private void MovePreControlFileScriptStepDown()
    {
        if (!EnsureAuthenticated() || SelectedControlFileScriptBinding is null || SelectedPreControlFileScriptStep is null)
        {
            return;
        }

        var currentIndex = SelectedControlFileScriptBinding.PreExecutionSteps.IndexOf(SelectedPreControlFileScriptStep);
        if (currentIndex < 0 || currentIndex >= SelectedControlFileScriptBinding.PreExecutionSteps.Count - 1)
        {
            return;
        }

        SelectedControlFileScriptBinding.PreExecutionSteps.Move(currentIndex, currentIndex + 1);
        PersistSettings("Pre-execution control-file sequence order updated.");
    }

    [RelayCommand]
    private void AddPostControlFileScriptStep()
    {
        if (!EnsureAuthenticated() || SelectedControlFileScriptBinding is null)
        {
            return;
        }

        var step = new ControlFileScriptSequenceStep
        {
            ScriptId = ProjectScriptEntries.FirstOrDefault()?.Id ?? string.Empty,
            OnSuccess = ControlFileScriptOutcomeAction.RunNextScript,
            OnFailure = ControlFileScriptOutcomeAction.AbortControlFlow
        };

        SelectedControlFileScriptBinding.PostExecutionSteps.Add(step);
        SelectedPostControlFileScriptStep = step;
        ActiveControlFileScriptStep = step;
        ActiveControlFileScriptPhase = "post";
        PersistSettings("Post-execution control-file sequence updated.");
    }

    [RelayCommand]
    private void RemoveSelectedPostControlFileScriptStep()
    {
        if (!EnsureAuthenticated() || SelectedControlFileScriptBinding is null || SelectedPostControlFileScriptStep is null)
        {
            return;
        }

        SelectedControlFileScriptBinding.PostExecutionSteps.Remove(SelectedPostControlFileScriptStep);
        SelectedPostControlFileScriptStep = SelectedControlFileScriptBinding.PostExecutionSteps.FirstOrDefault();
        ActiveControlFileScriptStep = SelectedPostControlFileScriptStep;
        PersistSettings("Post-execution control-file sequence updated.");
    }

    [RelayCommand]
    private void AddActiveControlFileParameterOverride()
    {
        if (!EnsureAuthenticated() || ActiveControlFileScriptStep is null)
        {
            return;
        }

        ActiveControlFileScriptStep.ParameterOverrides.Add(new ControlFileScriptParameterOverrideEntry
        {
            Name = "ParameterName",
            Value = string.Empty
        });
        SelectedActiveControlFileParameterOverride = ActiveControlFileScriptStep.ParameterOverrides.LastOrDefault();
        PersistSettings("Control file script parameter overrides updated.");
    }

    [RelayCommand]
    private void RemoveActiveControlFileParameterOverride(ControlFileScriptParameterOverrideEntry? overrideEntry)
    {
        if (!EnsureAuthenticated() || ActiveControlFileScriptStep is null || overrideEntry is null)
        {
            return;
        }

        ActiveControlFileScriptStep.ParameterOverrides.Remove(overrideEntry);
        SelectedActiveControlFileParameterOverride = ActiveControlFileScriptStep.ParameterOverrides.FirstOrDefault();
        PersistSettings("Control file script parameter overrides updated.");
    }

    [RelayCommand]
    private void MovePostControlFileScriptStepUp()
    {
        if (!EnsureAuthenticated() || SelectedControlFileScriptBinding is null || SelectedPostControlFileScriptStep is null)
        {
            return;
        }

        var currentIndex = SelectedControlFileScriptBinding.PostExecutionSteps.IndexOf(SelectedPostControlFileScriptStep);
        if (currentIndex <= 0)
        {
            return;
        }

        SelectedControlFileScriptBinding.PostExecutionSteps.Move(currentIndex, currentIndex - 1);
        PersistSettings("Post-execution control-file sequence order updated.");
    }

    [RelayCommand]
    private void MovePostControlFileScriptStepDown()
    {
        if (!EnsureAuthenticated() || SelectedControlFileScriptBinding is null || SelectedPostControlFileScriptStep is null)
        {
            return;
        }

        var currentIndex = SelectedControlFileScriptBinding.PostExecutionSteps.IndexOf(SelectedPostControlFileScriptStep);
        if (currentIndex < 0 || currentIndex >= SelectedControlFileScriptBinding.PostExecutionSteps.Count - 1)
        {
            return;
        }

        SelectedControlFileScriptBinding.PostExecutionSteps.Move(currentIndex, currentIndex + 1);
        PersistSettings("Post-execution control-file sequence order updated.");
    }
}