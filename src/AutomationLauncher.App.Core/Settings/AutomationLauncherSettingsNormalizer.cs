using System.Collections.ObjectModel;
using AutomationLauncher.Domain.Models;

namespace AutomationLauncher.App;

public static class AutomationLauncherSettingsNormalizer
{
    public static void Normalize(AutomationLauncherSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        settings.Archive ??= new();
        settings.Project ??= new();
        settings.ControlFiles ??= new();
        settings.Startup ??= new();
        settings.Logging ??= new();
        settings.Ui ??= new();

        settings.Project.PowerShellScripts ??= new List<ProjectScriptEntry>();
        settings.Project.PowerShellScripts = settings.Project.PowerShellScripts
            .Where(entry => entry is not null)
            .Select(entry => new ProjectScriptEntry
            {
                Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id,
                Name = entry.Name?.Trim() ?? string.Empty,
                ScriptBody = entry.ScriptBody ?? string.Empty,
                TimeoutSeconds = entry.TimeoutSeconds < 1 ? 300 : entry.TimeoutSeconds,
                Parameters = new ObservableCollection<ProjectScriptParameterEntry>(NormalizeScriptParameters(entry.Parameters))
            })
            .ToList();

        var rawBindings = settings.ControlFiles.Bindings ?? ControlFileScriptBinding.CreateDefaultBindings();
        var normalizedBindings = new List<ControlFileScriptBinding>();
        var seenTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in rawBindings)
        {
            var normalizedBinding = NormalizeControlFileBinding(binding);
            if (normalizedBinding is null || !seenTypes.Add(normalizedBinding.ControlFileType))
            {
                continue;
            }

            normalizedBindings.Add(normalizedBinding);
        }

        settings.ControlFiles.Bindings = normalizedBindings;

        settings.Startup.SplashBackgroundImagePath = string.IsNullOrWhiteSpace(settings.Startup.SplashBackgroundImagePath)
            ? string.Empty
            : settings.Startup.SplashBackgroundImagePath.Trim();

        settings.Startup.SequenceEntries ??= new List<StartupSequenceEntry>();
        settings.Startup.SequenceEntries = settings.Startup.SequenceEntries
            .Where(entry => entry is not null)
            .Select(entry => new StartupSequenceEntry
            {
                Alias = entry.Alias?.Trim() ?? string.Empty,
                ExecutablePath = entry.ExecutablePath?.Trim() ?? string.Empty,
                DelaySeconds = Math.Max(0, entry.DelaySeconds)
            })
            .ToList();

        if (settings.Logging.RetainedFileCountLimit < 1)
        {
            settings.Logging.RetainedFileCountLimit = 30;
        }

        settings.Logging.DirectoryPath = string.IsNullOrWhiteSpace(settings.Logging.DirectoryPath)
            ? "logs"
            : settings.Logging.DirectoryPath.Trim();

        settings.Logging.MinimumLevel = string.IsNullOrWhiteSpace(settings.Logging.MinimumLevel)
            ? "Information"
            : settings.Logging.MinimumLevel.Trim();

        settings.Ui.ControlFilesDirectory = string.IsNullOrWhiteSpace(settings.Ui.ControlFilesDirectory)
            ? string.Empty
            : settings.Ui.ControlFilesDirectory.Trim();

        if (!Enum.IsDefined(typeof(ArchiveBackupFlow), settings.Archive.BackupFlow))
        {
            settings.Archive.BackupFlow = ArchiveBackupFlow.TimestampedRetention;
        }

        if (settings.Archive.SuccessfulBackupRetentionCount < 0)
        {
            settings.Archive.SuccessfulBackupRetentionCount = 0;
        }
    }

    private static ControlFileScriptBinding? NormalizeControlFileBinding(ControlFileScriptBinding? binding)
    {
        if (binding is null
            || !ControlFileScriptBinding.TryNormalizeControlFileType(binding.ControlFileType, out var normalizedType)
            || ControlFileScriptBinding.IsReservedMarkerType(normalizedType))
        {
            return null;
        }

        var normalizedAction = NormalizeControlCommandAction(binding.Action, normalizedType);
        return new ControlFileScriptBinding
        {
            ControlFileType = normalizedType,
            DisplayName = string.IsNullOrWhiteSpace(binding.DisplayName) ? string.Empty : binding.DisplayName.Trim(),
            Action = normalizedAction,
            SplashTitle = string.IsNullOrWhiteSpace(binding.SplashTitle) ? string.Empty : binding.SplashTitle.Trim(),
            SplashBackgroundImagePath = string.IsNullOrWhiteSpace(binding.SplashBackgroundImagePath) ? string.Empty : binding.SplashBackgroundImagePath.Trim(),
            SplashCountdownSeconds = binding.SplashCountdownSeconds < 0
                ? ControlFileScriptBinding.BuildDefaultSplashCountdownSeconds(normalizedAction)
                : binding.SplashCountdownSeconds,
            PreExecutionSteps = new ObservableCollection<ControlFileScriptSequenceStep>(NormalizeSequenceSteps(binding.PreExecutionSteps)),
            PostExecutionSteps = new ObservableCollection<ControlFileScriptSequenceStep>(NormalizeSequenceSteps(binding.PostExecutionSteps))
        };
    }

    private static HostControlCommandAction NormalizeControlCommandAction(HostControlCommandAction action, string normalizedType)
    {
        if (action is HostControlCommandAction.Start or HostControlCommandAction.Stop or HostControlCommandAction.Archive)
        {
            return action;
        }

        return normalizedType switch
        {
            "stop" => HostControlCommandAction.Stop,
            "march" => HostControlCommandAction.Archive,
            "archive" => HostControlCommandAction.Archive,
            _ => HostControlCommandAction.Start
        };
    }

    private static IEnumerable<ControlFileScriptSequenceStep> NormalizeSequenceSteps(IEnumerable<ControlFileScriptSequenceStep>? steps)
    {
        return (steps ?? Enumerable.Empty<ControlFileScriptSequenceStep>())
            .Where(step => step is not null)
            .Select(step => new ControlFileScriptSequenceStep
            {
                ScriptId = step.ScriptId?.Trim() ?? string.Empty,
                OnSuccess = Enum.IsDefined(typeof(ControlFileScriptOutcomeAction), step.OnSuccess)
                    ? step.OnSuccess
                    : ControlFileScriptOutcomeAction.RunNextScript,
                OnFailure = Enum.IsDefined(typeof(ControlFileScriptOutcomeAction), step.OnFailure)
                    ? step.OnFailure
                    : ControlFileScriptOutcomeAction.AbortControlFlow,
                ParameterOverrides = new ObservableCollection<ControlFileScriptParameterOverrideEntry>(NormalizeParameterOverrides(step.ParameterOverrides))
            });
    }

    private static IEnumerable<ControlFileScriptParameterOverrideEntry> NormalizeParameterOverrides(IEnumerable<ControlFileScriptParameterOverrideEntry>? overrides)
    {
        return (overrides ?? Enumerable.Empty<ControlFileScriptParameterOverrideEntry>())
            .Where(overrideEntry => overrideEntry is not null)
            .Select(overrideEntry => new ControlFileScriptParameterOverrideEntry
            {
                Name = overrideEntry.Name?.Trim() ?? string.Empty,
                Value = overrideEntry.Value ?? string.Empty
            })
            .Where(overrideEntry => !string.IsNullOrWhiteSpace(overrideEntry.Name));
    }

    private static IEnumerable<ProjectScriptParameterEntry> NormalizeScriptParameters(IEnumerable<ProjectScriptParameterEntry>? parameters)
    {
        return (parameters ?? Enumerable.Empty<ProjectScriptParameterEntry>())
            .Where(parameter => parameter is not null)
            .Select(parameter => new ProjectScriptParameterEntry
            {
                Name = parameter.Name?.Trim() ?? string.Empty,
                DefaultValue = parameter.DefaultValue ?? string.Empty
            })
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name));
    }
}
