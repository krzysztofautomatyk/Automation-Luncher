using System.Linq;
using System.IO;
using System.Text.Json;
using System.Windows;
using AutomationLauncher.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace AutomationLauncher.App;

public partial class MainWindowViewModel : ObservableObject
{
    private void RefreshProjectScriptPreview()
    {
        if (SelectedProjectScriptEntry is null)
        {
            ProjectScriptPreview = string.Empty;
            return;
        }

        ProjectScriptPreview = _projectScriptWorkflowService.BuildManualPreview(
            SelectedProjectScriptEntry,
            CurrentHostControlState,
            ControlFilesFolderPath);
    }

    private void RefreshControlFileStepPreview()
    {
        if (SelectedControlFileScriptBinding is null || ActiveControlFileScriptStep is null)
        {
            ControlFileStepPreviewStatus = "Select a control-file step to edit parameter overrides and preview the final script.";
            ControlFileStepPreview = string.Empty;
            return;
        }

        var preview = _projectScriptWorkflowService.BuildControlFileStepPreview(
            ProjectScriptEntries,
            SelectedControlFileScriptBinding,
            ActiveControlFileScriptStep,
            ActiveControlFileScriptPhase,
            CurrentHostControlState,
            ControlFilesFolderPath);

        ControlFileStepPreviewStatus = preview.Status;
        ControlFileStepPreview = preview.Preview;
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
            var result = await _projectScriptWorkflowService.RunManualScriptAsync(
                script,
                CurrentHostControlState,
                ControlFilesFolderPath,
                CancellationToken.None);

            script.LastRunFinishedAt = result.FinishedAt;
            script.LastExitCode = result.ExitCode;
            script.LastOutput = result.CombinedOutput;
            script.LastRunStatus = result.StatusMessage;
            ProjectScriptExecutionStatus = result.StatusMessage;
            ProjectScriptExecutionOutput = result.CombinedOutput;

            AddHistory(
                result.IsSuccess ? "OK" : "ERROR",
                result.IsSuccess
                    ? "ProjectScriptSucceeded"
                    : result.IsRunnerError
                        ? "ProjectScriptRunnerFailed"
                        : "ProjectScriptFailed",
                $"{result.ScriptLabel}: {result.StatusMessage}");
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
            AutomationLauncherSettingsNormalizer.Normalize(_settings);
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
    private void AddControlFileCommandVariant()
    {
        if (!EnsureAuthenticated())
        {
            return;
        }

        var action = SelectedControlFileScriptBinding?.Action is HostControlCommandAction.Start or HostControlCommandAction.Stop or HostControlCommandAction.Archive
            ? SelectedControlFileScriptBinding.Action
            : HostControlCommandAction.Start;
        var controlFileType = GenerateUniqueControlFileType(action);
        var binding = new ControlFileScriptBinding
        {
            Action = action,
            ControlFileType = controlFileType,
            SplashCountdownSeconds = ControlFileScriptBinding.BuildDefaultSplashCountdownSeconds(action)
        };

        ControlFileScriptBindings.Add(binding);
        SelectedControlFileScriptBinding = binding;
        PersistSettings("Control file command variant added.");
    }

    [RelayCommand]
    private void RemoveSelectedControlFileCommandVariant()
    {
        if (!EnsureAuthenticated() || SelectedControlFileScriptBinding is null)
        {
            return;
        }

        var bindingToRemove = SelectedControlFileScriptBinding;
        ControlFileScriptBindings.Remove(bindingToRemove);
        SelectedControlFileScriptBinding = ControlFileScriptBindings.FirstOrDefault();
        PersistSettings("Control file command variant removed.");
    }

    [RelayCommand]
    private void BrowseSelectedControlFileSplashBackground()
    {
        if (!EnsureAuthenticated() || SelectedControlFileScriptBinding is null)
        {
            return;
        }

        var dialog = new FileDialog
        {
            Title = $"Select splash screen background for {SelectedControlFileScriptBinding.EffectiveDisplayName}",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        SelectedControlFileScriptBinding.SplashBackgroundImagePath = dialog.FileName;
    }

    [RelayCommand]
    private void ClearSelectedControlFileSplashBackground()
    {
        if (!EnsureAuthenticated() || SelectedControlFileScriptBinding is null)
        {
            return;
        }

        SelectedControlFileScriptBinding.SplashBackgroundImagePath = string.Empty;
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

    private string GenerateUniqueControlFileType(HostControlCommandAction action)
    {
        var seed = action switch
        {
            HostControlCommandAction.Stop => "stop-variant",
            HostControlCommandAction.Archive => "archive-variant",
            _ => "start-variant"
        };

        var existingTypes = new HashSet<string>(
            ControlFileScriptBindings
                .Select(binding => binding.ControlFileType)
                .Where(controlFileType => ControlFileScriptBinding.TryNormalizeControlFileType(controlFileType, out _)),
            StringComparer.OrdinalIgnoreCase);

        if (!existingTypes.Contains(seed))
        {
            return seed;
        }

        for (var index = 2; ; index++)
        {
            var candidate = $"{seed}-{index}";
            if (!existingTypes.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
